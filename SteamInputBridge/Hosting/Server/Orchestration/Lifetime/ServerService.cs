using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Forwarding.Controller.Routing;
using SteamInputBridge.Forwarding.Mouse;
using SteamInputBridge.Hosting.Server.Orchestration.Active;
using SteamInputBridge.Hosting.Server.Pipes;
using SteamInputBridge.Runtime;
using SteamInputBridge.Settings;
using SteamInputBridge.Settings.Profiles;

namespace SteamInputBridge.Hosting.Server.Orchestration.Lifetime;

/// <summary>Long-lived local server for client connections.</summary>
public sealed class ServerService : IAsyncDisposable
{
    private const string PipeName = "SteamInputBridge";
    private const string InstanceSemaphoreName = @"Local\SteamInputBridge.Server";
    private static readonly TimeSpan StartupCleanupTimeout = TimeSpan.FromSeconds(5);

    private readonly ILogger<ServerService> _logger;
    private readonly SettingsFile? _settingsFile;
    private readonly string _pipeName;
    private readonly ServerConnectionTracker _connections = new();
    private readonly ServerSessions _sessions;
    private readonly ServerActiveClientLoop _activeClients;
    private readonly ControllerBroker _controllerBroker;
    private readonly MouseBroker _mouseBroker;
    private readonly ControllerPipeSessions _controllerPipes;
    private readonly MouseInputPump _mouseInput;
    private readonly PhysicalControllerPump _physicalControllers;
    private readonly ServerShortcutService? _shortcuts;
    private readonly Func<CancellationToken, Task> _startupCleanup;

    /// <summary>Raised when a server state change should refresh status consumers.</summary>
    public event EventHandler? StatusChanged;

    // MARK: Construction
    // ========================================================================

    /// <summary>Creates a server.</summary>
    public ServerService(ILogger<ServerService> logger)
        : this(
            logger,
            settingsFile: null,
            profiles: null,
            runtime: null,
            activeClients: null)
    {
    }

    internal ServerService(
        ILogger<ServerService> logger,
        SettingsFile? settingsFile,
        ProfilesService? profiles,
        ActiveClientRegistry? runtime,
        ServerActiveClientLoop? activeClients,
        ControllerBroker? forwarding = null,
        MouseBroker? mouseForwarding = null,
        ServerShortcutService? shortcuts = null,
        Func<CancellationToken, Task>? startupCleanup = null,
        string? pipeName = null)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
        _settingsFile = settingsFile;
        _pipeName = string.IsNullOrWhiteSpace(pipeName)
            ? PipeName
            : pipeName;
        _startupCleanup = startupCleanup ?? (static _ => Task.CompletedTask);
        _shortcuts = shortcuts;
        _controllerBroker = forwarding ?? new ControllerBroker(new NoopControllerOutputFactory());
        _mouseBroker = mouseForwarding ?? new MouseBroker(new NoopMouseOutputFactory());
        _physicalControllers = new PhysicalControllerPump(_controllerBroker, logger);
        _controllerPipes = new ControllerPipeSessions(_controllerBroker, logger, _physicalControllers);

        ActiveClientRegistry activeRuntime = runtime ?? new ActiveClientRegistry();
        _mouseInput = new MouseInputPump(_mouseBroker, logger);
        _activeClients = activeClients ?? ServerActiveClientLoop.CreateDefault(
            activeRuntime,
            logger,
            _controllerBroker,
            _mouseBroker,
            NotifyStatusChanged);

        _sessions = new ServerSessions(
            logger,
            profiles,
            activeRuntime,
            _controllerBroker,
            _mouseBroker,
            _controllerPipes,
            () => new ServerInputStatus(_mouseInput.GetStatus(), _physicalControllers.GetStatus()),
            () => _activeClients.GetSteamInputStatus(),
            () => _shortcuts?.GetOverlayStatus() ?? OverlayStatus.Hidden,
            () => _shortcuts?.GetShortcutStatus() ?? ShortcutRuntimeStatus.Empty,
            statusChanged: NotifyStatusChanged);

        _physicalControllers.ControllersChanged += OnPhysicalControllersChanged;
        SubscribeShortcutStateChanged();
    }

    internal IReadOnlyCollection<ConnectedClient> Clients => _sessions.Clients;

    // MARK: Publics
    // ========================================================================

    /// <summary>Runs the server until cancellation.</summary>
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Accepted pipe ownership transfers to a tracked connection handle.")]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        string pipeName = _pipeName;
        using ServerInstanceLease? instanceLease = TryAcquireInstanceLease();
        HostingLog.ListeningOnServerPipe(_logger, pipeName);

        if (_settingsFile is not null)
        {
            HostingLog.UsingSettingsFile(_logger, _settingsFile.Path);
        }

        await RunStartupCleanupAsync(cancellationToken).ConfigureAwait(false);

        using CancellationTokenSource orchestrationStop =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task orchestrationTask = _activeClients.RunAsync(orchestrationStop.Token);
        _mouseInput.Start(orchestrationStop.Token);
        _physicalControllers.Start(orchestrationStop.Token);
        _shortcuts?.Start(orchestrationStop.Token);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                NamedPipeServerStream? pipe = new(
                    pipeName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 254,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                ServerConnectionHandle? connection = null;

                try
                {
                    await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                    connection = ServerConnectionHandle.Start(pipe, _sessions, cancellationToken);
                    pipe = null;
                    TrackConnection(connection);
                    connection = null;
                }
                finally
                {
                    if (connection is not null)
                    {
                        await connection.DisposeAsync().ConfigureAwait(false);
                    }

                    if (pipe is not null)
                    {
                        await pipe.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
        }
        finally
        {
            await orchestrationStop.CancelAsync().ConfigureAwait(false);
            await IgnoreCancellationAsync(orchestrationTask).ConfigureAwait(false);
            _physicalControllers.ControllersChanged -= OnPhysicalControllersChanged;
            UnsubscribeShortcutStateChanged();

            await _mouseInput.DisposeAsync().ConfigureAwait(false);
            await _physicalControllers.DisposeAsync().ConfigureAwait(false);
            _shortcuts?.Dispose();
            await _connections.DisposeAsync().ConfigureAwait(false);
            await DisposeForwardingAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Gets current server status.</summary>
    public Task<ServerStatus> GetStatusAsync()
    {
        return _sessions.GetStatusAsync();
    }

    /// <summary>Gets the tray overlay status without building full diagnostics.</summary>
    public OverlayStatus GetOverlayStatus()
    {
        return _shortcuts?.GetOverlayStatus() ?? OverlayStatus.Hidden;
    }

    /// <summary>Stops a connected client process and releases its server routes.</summary>
    public Task StopClientAsync(Guid clientId)
    {
        return _sessions.StopClientAsync(clientId);
    }

    /// <summary>Stops server-owned pumps.</summary>
    public async ValueTask DisposeAsync()
    {
        _physicalControllers.ControllersChanged -= OnPhysicalControllersChanged;
        UnsubscribeShortcutStateChanged();

        await _mouseInput.DisposeAsync().ConfigureAwait(false);
        await _physicalControllers.DisposeAsync().ConfigureAwait(false);
        _shortcuts?.Dispose();
        await _connections.DisposeAsync().ConfigureAwait(false);
        await DisposeForwardingAsync().ConfigureAwait(false);
    }

    // MARK: Privates
    // ========================================================================

    private async Task DisposeForwardingAsync()
    {
        await _controllerPipes.DisposeAsync().ConfigureAwait(false);
        await _controllerBroker.DisposeAsync().ConfigureAwait(false);
        await _mouseBroker.DisposeAsync().ConfigureAwait(false);
    }

    private void OnPhysicalControllersChanged()
    {
        _ = _controllerPipes.RefreshControllerRoutes();
        NotifyStatusChanged();
    }

    private void NotifyStatusChanged()
    {
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SubscribeShortcutStateChanged()
    {
        _shortcuts?.StateChanged += NotifyStatusChanged;
    }

    private void UnsubscribeShortcutStateChanged()
    {
        _shortcuts?.StateChanged -= NotifyStatusChanged;
    }

    private void TrackConnection(ServerConnectionHandle connection)
    {
        _connections.Track(connection);
    }

    private ServerInstanceLease? TryAcquireInstanceLease()
    {
        if (!string.Equals(_pipeName, PipeName, StringComparison.Ordinal))
        {
            return null;
        }

        Semaphore? semaphore = null;
        try
        {
            semaphore = new Semaphore(1, 1, InstanceSemaphoreName);
            if (semaphore.WaitOne(0))
            {
                Semaphore acquired = semaphore;
                semaphore = null;
                return new ServerInstanceLease(acquired);
            }

            throw new InvalidOperationException("Another Steam Input Bridge server is already running.");
        }
        finally
        {
            semaphore?.Dispose();
        }
    }

    private async Task RunStartupCleanupAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(StartupCleanupTimeout);

        try
        {
            await _startupCleanup(timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            HostingLog.StartupCleanupDidNotFinish(_logger, "timed out");
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
                IOException or
                ObjectDisposedException)
        {
            HostingLog.StartupCleanupDidNotFinish(_logger, exception.Message);
        }
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

    private sealed class ServerInstanceLease(Semaphore semaphore) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _ = semaphore.Release();
            semaphore.Dispose();
        }
    }
}
