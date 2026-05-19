using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualMouse.Forwarding;
using VirtualMouse.Runtime;
using VirtualMouse.Settings;
using VirtualMouse.Settings.Profiles;

namespace VirtualMouse.Hosting;

// The app-facing server owns server lifetime and accepts client pipes.
/// <summary>Long-lived local server for client connections.</summary>
public sealed class ServerService : IAsyncDisposable
{
    private readonly IOptions<HostingSettings> _options;
    private readonly ILogger<ServerService> _logger;
    private readonly SettingsFile? _settingsFile;
    private readonly ConcurrentDictionary<ServerConnectionHandle, byte> _connections = [];
    private readonly ServerSessions _sessions;
    private readonly ServerActiveClientLoop _activeClients;
    private readonly PhysicalControllerPump _physicalControllers;
    private readonly MouseInputPump _mouseInput;
    private readonly Func<CancellationToken, Task> _startupCleanup;

    // MARK: Construction
    // ========================================================================

    /// <summary>Creates a server from configured hosting settings.</summary>
    public ServerService(
        IOptions<HostingSettings> options,
        ILogger<ServerService> logger)
        : this(
            options ?? throw new ArgumentNullException(nameof(options)),
            logger,
            settingsFile: null,
            profiles: null,
            runtime: null,
            activeClients: null)
    {
    }

    internal ServerService(
        IOptions<HostingSettings> options,
        ILogger<ServerService> logger,
        SettingsFile? settingsFile,
        ProfilesService? profiles,
        ActiveClientRegistry? runtime,
        ServerActiveClientLoop? activeClients,
        ControllerBroker? forwarding = null,
        MouseBroker? mouseForwarding = null,
        Func<CancellationToken, Task>? startupCleanup = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options;
        _logger = logger;
        _settingsFile = settingsFile;
        _startupCleanup = startupCleanup ?? (static _ => Task.CompletedTask);

        ActiveClientRegistry activeRuntime = runtime ?? new ActiveClientRegistry();
        ControllerBroker? broker = forwarding ?? new ControllerBroker(new NoopControllerOutputFactory());
        MouseBroker? mouseBroker = mouseForwarding ?? new MouseBroker(new NoopMouseOutputFactory());
        ControllerPipeSessions? controllerPipes = new(broker, logger);
        try
        {
            _physicalControllers = new PhysicalControllerPump(broker, logger);
            _mouseInput = new MouseInputPump(mouseBroker, logger);

            _sessions = new ServerSessions(
                logger,
                profiles,
                activeRuntime,
                broker,
                mouseBroker,
                controllerPipes,
                () => new ServerInputStatus(
                    _physicalControllers.GetStatus(),
                    _mouseInput.GetStatus()));

            _activeClients = activeClients ?? ServerActiveClientLoop.CreateDefault(
                activeRuntime,
                options.Value,
                logger,
                broker,
                mouseBroker);

            broker = null;
            mouseBroker = null;
            controllerPipes = null;
        }
        finally
        {
            controllerPipes?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            broker?.Dispose();
            mouseBroker?.Dispose();
        }
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
        string pipeName = _options.Value.PipeName;
        HostingLog.ListeningOnServerPipe(_logger, pipeName);

        if (_settingsFile is not null)
        {
            HostingLog.UsingSettingsFile(_logger, _settingsFile.Path);
        }

        await _startupCleanup(cancellationToken).ConfigureAwait(false);

        using CancellationTokenSource orchestrationStop =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task orchestrationTask = _activeClients.RunAsync(orchestrationStop.Token);
        _physicalControllers.Start(orchestrationStop.Token);
        _mouseInput.Start(orchestrationStop.Token);

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
            await DisposeConnectionsAsync().ConfigureAwait(false);
            await _sessions.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Stops server-owned pumps.</summary>
    public async ValueTask DisposeAsync()
    {
        await _physicalControllers.DisposeAsync().ConfigureAwait(false);
        await _mouseInput.DisposeAsync().ConfigureAwait(false);
        await DisposeConnectionsAsync().ConfigureAwait(false);
        await _sessions.DisposeAsync().ConfigureAwait(false);
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
