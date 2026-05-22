using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Hosting.Client.Connection;
using SteamInputBridge.Hosting.Client.Run.Controllers;
using SteamInputBridge.Runtime;
using SteamInputBridge.Settings.Profiles;
using SteamInputBridge.Steam;
using StreamJsonRpc;

namespace SteamInputBridge.Hosting.Client.Run;

/// <summary>Runs one client-managed game and keeps server state restored.</summary>
public sealed class GameClient(
    ClientService client,
    ProfilesService profiles,
    ILogger<GameClient> logger) : IAsyncDisposable
{
    private readonly ClientGameProcessManager _processes = new(logger);
    private readonly ClientReceiverProcessMonitor _receivers = new(logger);
    private bool _disposed;

    // MARK: Publics
    // ========================================================================

    /// <summary>Launches a profile and reports receiver processes until it exits.</summary>
    public async Task RunAsync(
        string profileId,
        uint? steamAppId,
        bool killReceivers,
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

            ClientRunState state = new(launch, client.ClientId, request, killReceivers);
            _processes.StartProfileProcess(state);
            AppDomain.CurrentDomain.ProcessExit += ProcessExit;

            try
            {
                await StartControllerStreamsAsync(state, cancellationToken).ConfigureAwait(false);
                using CancellationTokenSource keepAliveStop =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                Task keepAlive = client.WaitAsync(keepAliveStop.Token);

                try
                {
                    _processes.LogStarted(state);
                    await _receivers
                        .WatchAsync(
                            state,
                            (observed, token) => SendStateAsync(state, observed, token),
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    _processes.StopGameProcesses(state, "Client run cancelled.");
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
                _processes.StopGameProcesses(state, "Client process exiting.");
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
        return RunAsync(profileId, steamAppId: null, killReceivers: false, cancellationToken);
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
        if (state.Launch.ControllerOutput == ControllerOutput.None)
        {
            return;
        }

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

    private uint? ResolveSteamAppId(string profileId, uint? overrideAppId)
    {
        if (overrideAppId.HasValue)
        {
            return ValidateSteamAppId(overrideAppId.Value);
        }

        GameProfile? profile = profiles.GetProfile(profileId);
        return profile?.SteamAppId is uint profileAppId
            ? ValidateSteamAppId(profileAppId)
            : SteamInputClient.ResolveAppId();
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
}
