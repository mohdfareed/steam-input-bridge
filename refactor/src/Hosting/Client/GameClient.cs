using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;
using VirtualMouse.Runtime;

namespace VirtualMouse.Hosting;

/// <summary>Runs one client-managed game and keeps server state restored.</summary>
public sealed class GameClient(
    VirtualMouseClient client,
    ILogger<GameClient> logger) : IAsyncDisposable
{
    private bool _disposed;

    /// <summary>Launches a profile and reports receiver processes until it exits.</summary>
    public async Task RunAsync(string profileId, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        client.ConnectionChanged += OnConnectionChanged;
        try
        {
            await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
            ClientRunLaunch launch = await client.StartRunAsync(profileId, cancellationToken).ConfigureAwait(false);
            using Process process = GameProcessHost.Launch(launch);
            ClientRunState state = new(launch, process, client.ClientId);
            using CancellationTokenSource keepAliveStop =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Task keepAlive = client.WaitAsync(keepAliveStop.Token);

            try
            {
                LogStarted(state);
                await WatchReceiversAsync(profileId, state, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                int killed = GameProcessHost.Kill(state.OwnedProcesses);
                logger.LogInformation("Stopped owned receiver processes: {Count}", killed);
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
            client.ConnectionChanged -= OnConnectionChanged;
        }
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

    private async Task WatchReceiversAsync(
        string profileId,
        ClientRunState state,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            IReadOnlyList<ObservedGameProcess> observed =
                GameProcessHost.FindReceivers(state.Launch.ReceiverProcesses);
            await SendStateAsync(profileId, state, observed, cancellationToken).ConfigureAwait(false);

            state.SawReceiver |= observed.Count != 0;
            if (state.SawReceiver && observed.Count == 0)
            {
                return;
            }

            if (!state.SawReceiver && state.Process.HasExited)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task SendStateAsync(
        string profileId,
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
                state.Launch = await client.StartRunAsync(profileId, cancellationToken).ConfigureAwait(false);
                state.RegisteredClientId = client.ClientId;
                logger.LogInformation(
                    "Restored server registration for {ProfileId} client={ClientId}",
                    state.Launch.ProfileId,
                    client.ClientId);
            }

            await client.UpdateRunProcessesAsync(observed, cancellationToken).ConfigureAwait(false);
            state.OwnedProcesses = await client
                .GetOwnedReceiverProcessesAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (IsConnectionFailure(exception))
        {
            state.RegisteredClientId = null;
        }
    }

    private async Task EndRunAsync(ClientRunState state)
    {
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
        logger.LogInformation(
            "Connection changed: {State} client={ClientId}",
            update.State,
            update.ClientId?.ToString() ?? "none");
    }

    private void LogStarted(ClientRunState state)
    {
        logger.LogInformation(
            "Started {ProfileId} rootPid={ProcessId}",
            state.Launch.ProfileId,
            state.Process.Id);
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
        Process process,
        Guid? registeredClientId)
    {
        public ClientRunLaunch Launch { get; set; } = launch;

        public Process Process { get; } = process;

        public Guid? RegisteredClientId { get; set; } = registeredClientId;

        public bool SawReceiver { get; set; }

        public IReadOnlyList<ObservedGameProcess> OwnedProcesses { get; set; } = [];
    }
}
