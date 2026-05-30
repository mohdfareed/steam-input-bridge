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
    private static readonly TimeSpan RestoreRetryDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ControllerStreamStartTimeout = TimeSpan.FromSeconds(5);

    private readonly SemaphoreSlim _stateGate = new(1, 1);
    private readonly ClientGameProcessManager _processes = new(logger);
    private readonly ClientReceiverProcessMonitor _receivers = new(logger);
    private readonly Func<IClientControllerStreams> _createControllerStreams =
        () => new ClientControllerStreams(logger);
    private ClientRunState? _currentState;
    // Reconnects are edge-triggered; stale restore attempts must not block the
    // next server lease from re-registering this still-running profile.
    private int _restoreGeneration;
    private CancellationToken _runCancellationToken;
    private bool _disposed;

    // MARK: Publics
    // ========================================================================

    internal GameClient(
        ClientService client,
        ProfilesService profiles,
        ILogger<GameClient> logger,
        Func<IClientControllerStreams> createControllerStreams)
        : this(client, profiles, logger)
    {
        ArgumentNullException.ThrowIfNull(createControllerStreams);
        _createControllerStreams = createControllerStreams;
    }

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

            ClientRunLaunch launch = await StartRunWithReconnectAsync(request, cancellationToken)
                .ConfigureAwait(false);

            ClientRunState state = new(launch, client.ClientId, request);
            _currentState = state;
            _runCancellationToken = cancellationToken;
            _processes.StartProfileProcess(state);
            AppDomain.CurrentDomain.ProcessExit += ProcessExit;

            try
            {
                await StartControllerStreamsAsync(state, cancellationToken).ConfigureAwait(false);
                using CancellationTokenSource keepAliveStop =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                Task keepAlive = client.WaitAsync(keepAliveStop.Token);
                bool receiverEndedNaturally = false;

                try
                {
                    _processes.LogStarted(state);
                    await _receivers
                        .WatchAsync(
                            state,
                            (observed, token) => SendStateAsync(state, observed, token),
                            cancellationToken)
                        .ConfigureAwait(false);
                    receiverEndedNaturally = true;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    _processes.StopGameProcesses(state, "Client run cancelled.");
                }
                finally
                {
                    if (!receiverEndedNaturally)
                    {
                        _processes.StopGameProcesses(state, "Client run ending.");
                    }

                    _currentState = null;
                    await EndRunAsync(state).ConfigureAwait(false);
                    await keepAliveStop.CancelAsync().ConfigureAwait(false);
                    await IgnoreExpectedStopAsync(keepAlive).ConfigureAwait(false);
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
        _stateGate.Dispose();
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
            await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!await RestoreServerRegistrationAsync(state, cancellationToken).ConfigureAwait(false))
                {
                    return;
                }

                await client.UpdateRunProcessesAsync(observed, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _ = _stateGate.Release();
            }
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
        if (update.State == ClientConnectionState.Disconnected)
        {
            _ = Interlocked.Increment(ref _restoreGeneration);
            if (_currentState is { } disconnected)
            {
                disconnected.RegisteredClientId = null;
            }

            return;
        }

        if (update.State == ClientConnectionState.Connected && _currentState is not null)
        {
            StartRestoreTask();
        }
    }

    private void StartRestoreTask()
    {
        int generation = Interlocked.Increment(ref _restoreGeneration);
        _ = Task.Run(() => RestoreConnectedRunAsync(generation), CancellationToken.None);
    }

    private async Task StartControllerStreamsAsync(
        ClientRunState state,
        CancellationToken cancellationToken)
    {
        if (state.Launch.ControllerOutput == ControllerOutput.None)
        {
            return;
        }

        IClientControllerStreams streams = _createControllerStreams();
        try
        {
            using CancellationTokenSource timeout =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ControllerStreamStartTimeout);
            await streams.StartAsync(client, state.Launch, timeout.Token).ConfigureAwait(false);
            state.ControllerStreams = streams;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await streams.DisposeAsync().ConfigureAwait(false);
            throw new TimeoutException("Timed out connecting the client controller stream pipe.");
        }
        catch
        {
            await streams.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task StopControllerStreamsAsync(ClientRunState state)
    {
        IClientControllerStreams? streams = state.ControllerStreams;
        state.ControllerStreams = null;
        if (streams is null)
        {
            return;
        }

        try
        {
            await streams.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (IsConnectionFailure(exception))
        {
            // Server restart invalidates the old controller pipe before the
            // restored run can open the replacement. Old-stream teardown is
            // best-effort; it must not abort re-registering the live client.
        }
    }

    private async Task RestoreConnectedRunAsync(int generation)
    {
        ClientRunState? state = _currentState;
        if (state is null)
        {
            return;
        }

        string? lastLoggedFailure = null;
        while (!_runCancellationToken.IsCancellationRequested &&
            ReferenceEquals(_currentState, state) &&
            !state.GameStopRequested)
        {
            if (generation != Volatile.Read(ref _restoreGeneration))
            {
                return;
            }

            if (client.State != ClientConnectionState.Connected)
            {
                await DelayRestoreRetryAsync().ConfigureAwait(false);
                continue;
            }

            if (state.RegisteredClientId == client.ClientId)
            {
                return;
            }

            try
            {
                await _stateGate.WaitAsync(_runCancellationToken).ConfigureAwait(false);
                try
                {
                    if (!ReferenceEquals(_currentState, state))
                    {
                        return;
                    }

                    if (await RestoreServerRegistrationAsync(state, _runCancellationToken).ConfigureAwait(false))
                    {
                        return;
                    }
                }
                finally
                {
                    _ = _stateGate.Release();
                }
            }
            catch (OperationCanceledException) when (_runCancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (IsConnectionFailure(exception))
            {
                state.RegisteredClientId = null;
                if (!string.Equals(lastLoggedFailure, exception.Message, StringComparison.Ordinal))
                {
                    HostingLog.ServerRegistrationRestoreRetrying(logger, exception.Message);
                    lastLoggedFailure = exception.Message;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                state.RegisteredClientId = null;
                if (!string.Equals(lastLoggedFailure, exception.Message, StringComparison.Ordinal))
                {
                    HostingLog.ServerRegistrationRestoreRetrying(logger, exception.Message);
                    lastLoggedFailure = exception.Message;
                }
            }

            await DelayRestoreRetryAsync().ConfigureAwait(false);
        }
    }

    private async Task DelayRestoreRetryAsync()
    {
        try
        {
            await Task.Delay(RestoreRetryDelay, _runCancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_runCancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task<bool> RestoreServerRegistrationAsync(
        ClientRunState state,
        CancellationToken cancellationToken)
    {
        if (client.State != ClientConnectionState.Connected)
        {
            state.RegisteredClientId = null;
            return false;
        }

        if (state.RegisteredClientId == client.ClientId)
        {
            return true;
        }

        await StopControllerStreamsAsync(state).ConfigureAwait(false);
        ClientRunLaunch launch = await StartRunWithReconnectAsync(state.Request, cancellationToken)
            .ConfigureAwait(false);
        state.Launch = launch;
        await StartControllerStreamsAsync(state, cancellationToken).ConfigureAwait(false);
        state.RegisteredClientId = client.ClientId;
        await RestoreRunProcessesAsync(state, cancellationToken).ConfigureAwait(false);
        HostingLog.RestoredServerRegistration(logger, state.Launch.ProfileId, client.ClientId);
        return true;
    }

    private async Task<ClientRunLaunch> StartRunWithReconnectAsync(
        StartRunRequest request,
        CancellationToken cancellationToken)
    {
        string? lastLoggedFailure = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                return await client.StartRunAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (IsConnectionFailure(exception))
            {
                // The keepalive loop starts after the initial run registration,
                // so StartRun has to repair a broken server pipe itself.
                if (!string.Equals(lastLoggedFailure, exception.Message, StringComparison.Ordinal))
                {
                    HostingLog.ServerRegistrationRestoreRetrying(logger, exception.Message);
                    lastLoggedFailure = exception.Message;
                }

                await client.ReconnectAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        throw new OperationCanceledException(cancellationToken);
    }

    private async Task RestoreRunProcessesAsync(
        ClientRunState state,
        CancellationToken cancellationToken)
    {
        // Server restarts lose active-process claims. The receiver monitor only
        // sends on process-list changes, so reconnect must push the current
        // snapshot even when the game kept running unchanged.
        IReadOnlyList<ObservedGameProcess> observed =
            GameProcessHost.FindReceivers(state.Launch.ReceiverProcesses);
        IReadOnlyList<ObservedGameProcess> receivers = state.UpdateReceivers(observed);
        await client.UpdateRunProcessesAsync(receivers, cancellationToken).ConfigureAwait(false);
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

    private static async Task IgnoreExpectedStopAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (IsConnectionFailure(exception))
        {
        }
    }

    private static bool IsConnectionFailure(Exception exception)
    {
        return exception is IOException
            or EndOfStreamException
            or InvalidOperationException
            or TimeoutException
            or ConnectionLostException
            or ObjectDisposedException;
    }
}
