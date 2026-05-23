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
using SteamInputBridge.HidHide;
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
    private readonly ServerHidHideDeviceResolver _hidHideDevices;
    private readonly HidHideService? _hidHide;
    private readonly ServerShortcutService? _shortcuts;
    private readonly Func<CancellationToken, Task> _startupCleanup;

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
        HidHideService? hidHide = null,
        HidHideDeviceCatalog? hidHideDevices = null,
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
        _hidHide = hidHide;
        _controllerBroker = forwarding ?? new ControllerBroker(new NoopControllerOutputFactory());
        _mouseBroker = mouseForwarding ?? new MouseBroker(new NoopMouseOutputFactory());
        _physicalControllers = new PhysicalControllerPump(_controllerBroker, logger);
        _controllerPipes = new ControllerPipeSessions(_controllerBroker, logger, _physicalControllers);
        _hidHideDevices = new ServerHidHideDeviceResolver(
            hidHideDevices,
            _controllerPipes);

        ActiveClientRegistry activeRuntime = runtime ?? new ActiveClientRegistry();
        _mouseInput = new MouseInputPump(_mouseBroker, logger);
        _activeClients = activeClients ?? ServerActiveClientLoop.CreateDefault(
            activeRuntime,
            logger,
            profiles,
            hidHide,
            _hidHideDevices.GetDevicePaths,
            _hidHideDevices.GetDeviceLabels,
            _controllerBroker,
            _mouseBroker);

        _sessions = new ServerSessions(
            logger,
            profiles,
            activeRuntime,
            _controllerBroker,
            _mouseBroker,
            _controllerPipes,
            () => new ServerInputStatus(_mouseInput.GetStatus(), _physicalControllers.GetStatus()),
            () => _activeClients.GetSteamInputStatus(),
            () => _activeClients.GetHidHideStatus(),
            () => _activeClients.RefreshHidHide());

        _physicalControllers.ControllersChanged += OnPhysicalControllersChanged;
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
        RegisterHidHideApplicationAccess();

        using CancellationTokenSource orchestrationStop =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task orchestrationTask = _activeClients.RunAsync(orchestrationStop.Token);
        _mouseInput.Start(orchestrationStop.Token);
        _physicalControllers.Start(orchestrationStop.Token);
        _shortcuts?.Start();

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
            await _mouseInput.DisposeAsync().ConfigureAwait(false);
            await _physicalControllers.DisposeAsync().ConfigureAwait(false);
            _shortcuts?.Dispose();
            _activeClients.ClearHidHide();
            await _connections.DisposeAsync().ConfigureAwait(false);
            await DisposeForwardingAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Gets current server status.</summary>
    public Task<ServerStatus> GetStatusAsync()
    {
        return _sessions.GetStatusAsync();
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
        await _mouseInput.DisposeAsync().ConfigureAwait(false);
        await _physicalControllers.DisposeAsync().ConfigureAwait(false);
        _shortcuts?.Dispose();
        _activeClients.ClearHidHide();
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
        _controllerPipes.RefreshControllerRoutes();
        _activeClients.RefreshHidHide();
    }

    private void TrackConnection(ServerConnectionHandle connection)
    {
        _connections.Track(connection);
    }

    private void RegisterHidHideApplicationAccess()
    {
        if (_hidHide is null)
        {
            return;
        }

        try
        {
            // HidHide normal mode uses one global app allow list. Keep this
            // process on that list for the server lifetime; scopes should only
            // hide devices and temporarily remove other apps.
            //
            // Steam-launched profile testing showed Steam Input still feeds the
            // client with only this executable allowed, so do not add Steam
            // here unless a concrete failing route proves it is needed.
            _hidHide.AllowCurrentProcess();
            HostingLog.HidHideApplicationAccessRegistered(_logger);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
                ArgumentException or
                System.ComponentModel.Win32Exception or
                IOException or
                UnauthorizedAccessException)
        {
            HostingLog.HidHideApplicationAccessFailed(_logger, exception.Message);
        }
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
