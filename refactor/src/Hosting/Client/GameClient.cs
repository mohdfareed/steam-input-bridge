using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;
using VirtualMouse.Runtime;
using VirtualMouse.Settings.Profiles;
using VirtualMouse.Steam;

namespace VirtualMouse.Hosting;

/// <summary>Runs one client-managed game and keeps server state restored.</summary>
public sealed class GameClient(
    ClientService client,
    ProfilesService profiles,
    ILogger<GameClient> logger) : IAsyncDisposable
{
    private bool _disposed;

    // MARK: Publics
    // ========================================================================

    /// <summary>Launches a profile and reports receiver processes until it exits.</summary>
    public async Task RunAsync(
        string profileId,
        uint? steamAppId,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        client.ConnectionChanged += OnConnectionChanged;
        try
        {
            StartRunRequest request = new(profileId, ResolveSteamAppId(profileId, steamAppId));
            await client.ConnectAsync(cancellationToken).ConfigureAwait(false);

            ClientRunLaunch launch = await client
                .StartRunAsync(request, cancellationToken)
                .ConfigureAwait(false);

            ClientRunState state = new(launch, client.ClientId, request);
            StartProfileProcess(state);
            AppDomain.CurrentDomain.ProcessExit += ProcessExit;

            try
            {
                await StartControllerStreamsAsync(state, cancellationToken).ConfigureAwait(false);
                using CancellationTokenSource keepAliveStop =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                Task keepAlive = client.WaitAsync(keepAliveStop.Token);

                try
                {
                    LogStarted(state);
                    LogReceiverWatch(state);
                    await WatchReceiversAsync(state, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    StopGameProcesses(state, "Client run cancelled.");
                }
                finally
                {
                    await EndRunAsync(state).ConfigureAwait(false);
                    await keepAliveStop.CancelAsync().ConfigureAwait(false);
                    await IgnoreCancellationAsync(keepAlive).ConfigureAwait(false);
                }
            }
            finally
            {
                AppDomain.CurrentDomain.ProcessExit -= ProcessExit;
                state.ProcessOwner?.Dispose();
                state.LaunchedProcess?.Dispose();
            }

            void ProcessExit(object? sender, EventArgs args)
            {
                _ = sender;
                _ = args;
                StopGameProcesses(state, "Client process exiting.");
            }
        }
        finally
        {
            client.ConnectionChanged -= OnConnectionChanged;
        }
    }

    /// <summary>Launches a profile and reads Steam app id from settings or Steam.</summary>
    public Task RunAsync(string profileId, CancellationToken cancellationToken)
    {
        return RunAsync(profileId, steamAppId: null, cancellationToken);
    }

    /// <summary>Disposes the underlying client.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await client.DisposeAsync().ConfigureAwait(false);
    }

    // MARK: Privates
    // ========================================================================

    private async Task WatchReceiversAsync(
        ClientRunState state,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            IReadOnlyList<ObservedGameProcess> observed =
                GameProcessHost.FindReceivers(state.Launch.ReceiverProcesses);
            LogReceiverChange(state, observed);
            await SendStateAsync(state, observed, cancellationToken).ConfigureAwait(false);

            state.SawReceiver |= observed.Count != 0;
            if (state.SawReceiver && observed.Count == 0)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task SendStateAsync(
        ClientRunState state,
        IReadOnlyList<ObservedGameProcess> observed,
        CancellationToken cancellationToken)
    {
        if (client.State != ClientConnectionState.Connected)
        {
            state.RegisteredClientId = null;
            return;
        }

        try
        {
            if (state.RegisteredClientId != client.ClientId)
            {
                await StopControllerStreamsAsync(state).ConfigureAwait(false);
                state.Launch = await client
                    .StartRunAsync(state.Request, cancellationToken)
                    .ConfigureAwait(false);
                state.RegisteredClientId = client.ClientId;
                await StartControllerStreamsAsync(state, cancellationToken).ConfigureAwait(false);
                HostingLog.RestoredServerRegistration(logger, state.Launch.ProfileId, client.ClientId);
            }

            await client.UpdateRunProcessesAsync(observed, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsConnectionFailure(exception))
        {
            state.RegisteredClientId = null;
        }
    }

    private async Task EndRunAsync(ClientRunState state)
    {
        await StopControllerStreamsAsync(state).ConfigureAwait(false);
        if (client.State != ClientConnectionState.Connected ||
            state.RegisteredClientId != client.ClientId)
        {
            return;
        }

        try
        {
            await client.EndRunAsync(CancellationToken.None).ConfigureAwait(false);
            state.RegisteredClientId = null;
        }
        catch (Exception exception) when (IsConnectionFailure(exception))
        {
        }
    }

    private void OnConnectionChanged(object? sender, ClientConnectionChangedEventArgs update)
    {
        HostingLog.ConnectionChanged(logger, update.State, update.ClientId);
    }

    private async Task StartControllerStreamsAsync(
        ClientRunState state,
        CancellationToken cancellationToken)
    {
        ClientControllerStreams streams = new(logger);
        await streams.StartAsync(client, state.Launch, cancellationToken).ConfigureAwait(false);
        state.ControllerStreams = streams;
    }

    private static async Task StopControllerStreamsAsync(ClientRunState state)
    {
        if (state.ControllerStreams is not null)
        {
            await state.ControllerStreams.DisposeAsync().ConfigureAwait(false);
            state.ControllerStreams = null;
        }
    }

    private void StartProfileProcess(ClientRunState state)
    {
        if (string.IsNullOrWhiteSpace(state.Launch.Executable))
        {
            return;
        }

        Process process = GameProcessHost.Launch(
            state.Launch.Executable,
            state.Launch.Arguments,
            state.Launch.WorkingDirectory ?? AppContext.BaseDirectory);
        state.LaunchedProcess = process;
        state.ProcessOwner = TryOwnProcessTree(process);
    }

    private void LogStarted(ClientRunState state)
    {
        if (state.LaunchedProcess is null)
        {
            HostingLog.Attached(logger, state.Launch.ProfileId);
            return;
        }

        HostingLog.Started(logger, state.Launch.ProfileId, state.LaunchedProcess.Id);
    }

    private void LogReceiverWatch(ClientRunState state)
    {
        if (!logger.IsEnabled(LogLevel.Information))
        {
            return;
        }

        string receivers = string.Join(", ", state.Launch.ReceiverProcesses);
        HostingLog.WatchingReceiverProcesses(logger, state.Launch.ProfileId, receivers);
    }

    private void LogReceiverChange(
        ClientRunState state,
        IReadOnlyList<ObservedGameProcess> observed)
    {
        string signature = string.Join(
            ",",
            observed.OrderBy(process => process.ProcessId).Select(process => process.ProcessId));
        if (signature == state.LastObservedSignature)
        {
            return;
        }

        state.LastObservedSignature = signature;
        HostingLog.ReceiverProcesses(
            logger,
            state.Launch.ProfileId,
            observed.Count,
            observed.Count == 0 ? "none" : FormatProcesses(observed));
    }

    private static string FormatProcesses(IReadOnlyList<ObservedGameProcess> processes)
    {
        return string.Join(
            ", ",
            processes
                .OrderBy(process => process.ProcessId)
                .Select(process => $"{process.ProcessName}:{process.ProcessId}"));
    }

    private void StopGameProcesses(ClientRunState state, string reason)
    {
        if (state.LaunchedProcess is null)
        {
            return;
        }

        lock (state.StopGate)
        {
            if (state.GameStopRequested)
            {
                return;
            }

            state.GameStopRequested = true;
        }

        int killed = GameProcessHost.KillLaunchedProcess(state.LaunchedProcess);
        HostingLog.StoppedGameProcesses(logger, reason, killed);
    }

    private IDisposable? TryOwnProcessTree(Process process)
    {
        try
        {
            return GameProcessHost.OwnProcessTree(process);
        }
        catch (Exception exception) when (
            exception is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            HostingLog.CouldNotAttachProcessJob(logger, exception.Message);
            return null;
        }
    }

    private uint? ResolveSteamAppId(string profileId, uint? overrideAppId)
    {
        if (overrideAppId.HasValue)
        {
            return ValidateSteamAppId(overrideAppId.Value);
        }

        GameProfile? profile = profiles.GetProfile(profileId);
        return profile?.SteamAppId is uint profileAppId
            ? ValidateSteamAppId(profileAppId)
            : SteamInputClient.ResolveAppIdFromEnvironment();
    }

    private static uint ValidateSteamAppId(uint appId)
    {
        return appId == 0
            ? throw new ArgumentOutOfRangeException(nameof(appId), "Steam app id must be greater than zero.")
            : appId;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static async Task IgnoreCancellationAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static bool IsConnectionFailure(Exception exception)
    {
        return exception is IOException
            or EndOfStreamException
            or InvalidOperationException
            or ConnectionLostException
            or ObjectDisposedException;
    }

    private sealed class ClientRunState(
        ClientRunLaunch launch,
        Guid? registeredClientId,
        StartRunRequest request)
    {
        public ClientRunLaunch Launch { get; set; } = launch;

        public Process? LaunchedProcess { get; set; }

        public IDisposable? ProcessOwner { get; set; }

        public Guid? RegisteredClientId { get; set; } = registeredClientId;

        public StartRunRequest Request { get; } = request;

        public bool SawReceiver { get; set; }

        public ClientControllerStreams? ControllerStreams { get; set; }

        public string? LastObservedSignature { get; set; }

        public object StopGate { get; } = new();

        public bool GameStopRequested { get; set; }
    }
}
