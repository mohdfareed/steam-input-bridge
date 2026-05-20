using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Forwarding.Controller;
using SteamInputBridge.Forwarding.Mouse;
using SteamInputBridge.HidHide;
using SteamInputBridge.Hosting.Server.Inputs;
using SteamInputBridge.Hosting.Server.Pipes;
using SteamInputBridge.Runtime;
using SteamInputBridge.Settings;
using SteamInputBridge.Settings.Profiles;

namespace SteamInputBridge.Hosting.Server;

// The app-facing server owns server lifetime and accepts client pipes.
/// <summary>Long-lived local server for client connections.</summary>
public sealed class ServerService : IAsyncDisposable
{
    private const string PipeName = "SteamInputBridge";

    private readonly ILogger<ServerService> _logger;
    private readonly SettingsFile? _settingsFile;
    private readonly ConcurrentDictionary<ServerConnectionHandle, byte> _connections = [];
    private readonly ServerSessions _sessions;
    private readonly ServerActiveClientLoop _activeClients;
    private readonly ControllerBroker _controllerBroker;
    private readonly MouseBroker _mouseBroker;
    private readonly ControllerPipeSessions _controllerPipes;
    private readonly PhysicalControllerPump _physicalControllers;
    private readonly MouseInputPump _mouseInput;
    private readonly HidHideProfileFirewall? _hidHide;
    private readonly HidHideApplicationAccess? _hidHideAccess;
    private readonly HidHideDeviceCatalog? _hidHideDevices;
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
        HidHideProfileFirewall? hidHide = null,
        HidHideApplicationAccess? hidHideAccess = null,
        HidHideDeviceCatalog? hidHideDevices = null,
        ServerShortcutService? shortcuts = null,
        Func<CancellationToken, Task>? startupCleanup = null)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
        _settingsFile = settingsFile;
        _startupCleanup = startupCleanup ?? (static _ => Task.CompletedTask);
        _hidHide = hidHide;
        _hidHideAccess = hidHideAccess;
        _hidHideDevices = hidHideDevices;
        _shortcuts = shortcuts;
        _controllerBroker = forwarding ?? new ControllerBroker(new NoopControllerOutputFactory());
        _mouseBroker = mouseForwarding ?? new MouseBroker(new NoopMouseOutputFactory());
        _controllerPipes = new ControllerPipeSessions(_controllerBroker, logger);

        ActiveClientRegistry activeRuntime = runtime ?? new ActiveClientRegistry();
        _physicalControllers = new PhysicalControllerPump(_controllerBroker, logger);
        _mouseInput = new MouseInputPump(_mouseBroker, logger);
        _activeClients = activeClients ?? ServerActiveClientLoop.CreateDefault(
            activeRuntime,
            logger,
            profiles,
            _hidHide,
            GetHidHideDevicePaths,
            _controllerBroker,
            _mouseBroker);

        _sessions = new ServerSessions(
            logger,
            profiles,
            activeRuntime,
            _controllerBroker,
            _mouseBroker,
            _controllerPipes,
            () => new ServerInputStatus(
                _physicalControllers.GetStatus(),
                _mouseInput.GetStatus()),
            () => _activeClients.GetSteamInputStatus(),
            () => _activeClients.GetHidHideStatus(),
            () => _activeClients.RefreshHidHide());
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
        string pipeName = PipeName;
        HostingLog.ListeningOnServerPipe(_logger, pipeName);

        if (_settingsFile is not null)
        {
            HostingLog.UsingSettingsFile(_logger, _settingsFile.Path);
        }

        await _startupCleanup(cancellationToken).ConfigureAwait(false);
        RegisterHidHideApplicationAccess();

        using CancellationTokenSource orchestrationStop =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task orchestrationTask = _activeClients.RunAsync(orchestrationStop.Token);
        _physicalControllers.Start(orchestrationStop.Token);
        _mouseInput.Start(orchestrationStop.Token);
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
            await _physicalControllers.DisposeAsync().ConfigureAwait(false);
            await _mouseInput.DisposeAsync().ConfigureAwait(false);
            _shortcuts?.Dispose();
            _hidHide?.Clear();
            await DisposeConnectionsAsync().ConfigureAwait(false);
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
        await _physicalControllers.DisposeAsync().ConfigureAwait(false);
        await _mouseInput.DisposeAsync().ConfigureAwait(false);
        _shortcuts?.Dispose();
        _hidHide?.Clear();
        await DisposeConnectionsAsync().ConfigureAwait(false);
        await DisposeForwardingAsync().ConfigureAwait(false);
    }

    // MARK: Privates
    // ========================================================================

    private async Task DisposeConnectionsAsync()
    {
        ServerConnectionHandle[] connections = [.. _connections.Keys];
        foreach (ServerConnectionHandle connection in connections)
        {
            _ = _connections.TryRemove(connection, out _);
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task DisposeForwardingAsync()
    {
        await _controllerPipes.DisposeAsync().ConfigureAwait(false);
        await _controllerBroker.DisposeAsync().ConfigureAwait(false);
        await _mouseBroker.DisposeAsync().ConfigureAwait(false);
    }

    private IReadOnlyList<string> GetHidHideDevicePaths(ActiveClientRegistryStatus status, Guid clientId)
    {
        _ = status;
        if (_hidHideDevices is null)
        {
            return [];
        }

        HashSet<string> devices = new(StringComparer.OrdinalIgnoreCase);
        foreach (ControllerPipeStatus pipe in _controllerPipes.GetStatus())
        {
            if (pipe.ClientId != clientId)
            {
                continue;
            }

            foreach (ClientControllerStatus controller in pipe.Controllers)
            {
                if (_hidHideDevices.FindDeviceInstancePath(GetControllerPath(controller.PhysicalControllerId)) is { } path)
                {
                    _ = devices.Add(path);
                }
            }
        }

        return [.. devices];
    }

    private static string? GetControllerPath(string physicalControllerId)
    {
        const string Prefix = "path:";
        return physicalControllerId.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
            ? physicalControllerId[Prefix.Length..]
            : null;
    }

    private void RegisterHidHideApplicationAccess()
    {
        if (_hidHideAccess is null)
        {
            return;
        }

        try
        {
            _hidHideAccess.AllowCurrentProcess();
            HostingLog.HidHideApplicationAccessRegistered(_logger);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
                ArgumentException or
                System.ComponentModel.Win32Exception or
                System.IO.IOException or
                UnauthorizedAccessException)
        {
            HostingLog.HidHideApplicationAccessFailed(_logger, exception.Message);
        }
    }

    private void TrackConnection(ServerConnectionHandle connection)
    {
        _connections[connection] = 0;
        _ = RemoveWhenCompleteAsync(connection);
    }

    private async Task RemoveWhenCompleteAsync(ServerConnectionHandle connection)
    {
        await IgnoreCancellationAsync(connection.Completion).ConfigureAwait(false);
        _ = _connections.TryRemove(connection, out _);
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

}
