using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Hosting.Client.Connection;
using SteamInputBridge.Hosting.Client.Run.Controllers;
using SteamInputBridge.Steam;
using StreamJsonRpc;
using ProfileControllerOutput = SteamInputBridge.Settings.Profiles.ControllerOutput;

namespace SteamInputBridge.Hosting.Client.Run;

/// <summary>Runs one client-managed game and keeps its server registration current.</summary>
public sealed class GameClient(
    ClientService client,
    ILogger<GameClient> logger) : IAsyncDisposable
{
    private static readonly TimeSpan ControllerStreamStartTimeout = TimeSpan.FromSeconds(5);

    private readonly SemaphoreSlim _stateGate = new(1, 1);
    private readonly ClientGameProcessManager _processes = new(logger);
    private readonly ClientReceiverProcessMonitor _receivers = new(logger);
    private readonly Func<IClientControllerStreams> _createControllerStreams =
        () => new ClientControllerStreams(logger);
    private ClientRunState? _currentState;
    private CancellationToken _runCancellationToken;
    private bool _disposed;

    // MARK: Publics
    // ========================================================================

    internal GameClient(
        ClientService client,
        ILogger<GameClient> logger,
        Func<IClientControllerStreams> createControllerStreams)
        : this(client, logger)
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
            RegisterRunRequest request = new(profileId, steamAppId ?? SteamInputClient.ResolveAppId());
            await client.ConnectAsync(cancellationToken).ConfigureAwait(false);

            ClientRunState state = new(request);
            _currentState = state;
            _runCancellationToken = cancellationToken;
            using CancellationTokenSource keepAliveStop =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Task keepAlive = client.WaitAsync(keepAliveStop.Token);
            await RegisterCurrentRunWithServerAsync().ConfigureAwait(false);
            _ = await state.FirstRegistration.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            _processes.StartProfileProcess(state);
            AppDomain.CurrentDomain.ProcessExit += ProcessExit;

            try
            {
                bool receiverEndedNaturally = false;

                try
                {
                    _processes.LogStarted(state);
                    await _receivers.WatchAsync(state, cancellationToken).ConfigureAwait(false);
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
            if (_currentState is { } disconnected)
            {
                disconnected.RegisteredClientId = null;
                _ = Task.Run(() => StopControllerStreamsAsync(disconnected), CancellationToken.None);
            }

            return;
        }

        if (update.State == ClientConnectionState.Connected && _currentState is not null)
        {
            _ = Task.Run(RegisterCurrentRunWithServerAsync, CancellationToken.None);
        }
    }

    private async Task StartControllerStreamsAsync(
        ClientRunState state,
        CancellationToken cancellationToken)
    {
        ClientRunLaunch launch = state.RegisteredLaunch;
        if (launch.ControllerOutput == ProfileControllerOutput.None)
        {
            return;
        }

        IClientControllerStreams streams = _createControllerStreams();
        try
        {
            using CancellationTokenSource timeout =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ControllerStreamStartTimeout);
            await streams.StartAsync(client, launch, timeout.Token).ConfigureAwait(false);
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
            // next registration can open the replacement. Old-stream teardown is
            // best-effort; it must not abort re-registering the live client.
        }
    }

    private async Task RegisterCurrentRunWithServerAsync()
    {
        ClientRunState? state = _currentState;
        if (state is null)
        {
            return;
        }

        if (_runCancellationToken.IsCancellationRequested ||
            state.GameStopRequested ||
            client.State != ClientConnectionState.Connected ||
            state.RegisteredClientId == client.ClientId)
        {
            return;
        }

        try
        {
            await _stateGate.WaitAsync(_runCancellationToken).ConfigureAwait(false);
            try
            {
                if (ReferenceEquals(_currentState, state))
                {
                    await RegisterRunWithServerAsync(state, _runCancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                _ = _stateGate.Release();
            }
        }
        catch (OperationCanceledException) when (_runCancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsConnectionFailure(exception))
        {
            state.RegisteredClientId = null;
            HostingLog.RunRegistrationFailed(logger, exception.Message);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            state.RegisteredClientId = null;
            HostingLog.RunRegistrationFailed(logger, exception.Message);
        }
    }

    private async Task RegisterRunWithServerAsync(
        ClientRunState state,
        CancellationToken cancellationToken)
    {
        if (client.State != ClientConnectionState.Connected)
        {
            state.RegisteredClientId = null;
            return;
        }

        if (state.RegisteredClientId == client.ClientId)
        {
            return;
        }

        try
        {
            await StopControllerStreamsAsync(state).ConfigureAwait(false);
            ClientRunLaunch launch = await client.RegisterRunAsync(state.Request, cancellationToken)
                .ConfigureAwait(false);
            state.Launch = launch;
            await StartControllerStreamsAsync(state, cancellationToken).ConfigureAwait(false);
            state.RegisteredClientId = client.ClientId;
            _ = state.FirstRegistration.TrySetResult(launch);
            HostingLog.RegisteredRunWithServer(logger, launch.ProfileId, client.ClientId);
        }
        catch (Exception exception) when (IsConnectionFailure(exception))
        {
            state.RegisteredClientId = null;
            await StopControllerStreamsAsync(state).ConfigureAwait(false);
            HostingLog.RunRegistrationFailed(logger, exception.Message);
        }
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
