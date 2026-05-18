using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualMouse.Forwarding;
using VirtualMouse.Runtime;
using VirtualMouse.Settings;
using VirtualMouse.Settings.Profiles;

namespace VirtualMouse.Hosting;

/// <summary>Current server status.</summary>
public sealed record ServerStatus(int ConnectedClientCount)
{
    /// <summary>Current active-client runtime status.</summary>
    public ActiveClientRegistryStatus Runtime { get; init; } =
        new(0, null, [], []);

    /// <summary>Current controller forwarding status.</summary>
    public ControllerBrokerStatus Forwarding { get; init; } =
        new(null, ControllerOutputEnabled: true, PhysicalMotionEnabled: true, []);
}

// The app-facing server owns server lifetime and accepts client pipes.
/// <summary>Long-lived local server for client connections.</summary>
public sealed class VirtualMouseServer
{
    private readonly IOptions<HostingSettings> _options;
    private readonly ILogger<VirtualMouseServer> _logger;
    private readonly SettingsFile? _settingsFile;
    private readonly ConcurrentDictionary<ServerConnection, byte> _connections = [];
    private readonly ServerSessions _sessions;
    private readonly ActiveClientOrchestration _activeClients;

    /// <summary>Creates a server from configured hosting settings.</summary>
    public VirtualMouseServer(
        IOptions<HostingSettings> options,
        ILogger<VirtualMouseServer> logger)
        : this(RequireOptions(options), logger, settingsFile: null, profiles: null, runtime: null, activeClients: null)
    {
    }

    internal VirtualMouseServer(
        IOptions<HostingSettings> options,
        ILogger<VirtualMouseServer> logger,
        SettingsFile? settingsFile,
        ProfilesService? profiles,
        ActiveClientRegistry? runtime,
        ActiveClientOrchestration? activeClients,
        ControllerBroker? forwarding = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options;
        _logger = logger;
        _settingsFile = settingsFile;

        ActiveClientRegistry activeRuntime = runtime ?? new ActiveClientRegistry();
        ControllerBroker broker = forwarding ?? new ControllerBroker(new NoopControllerOutputFactory());

        _sessions = new ServerSessions(
            logger,
            profiles,
            activeRuntime,
            broker,
            new ControllerPipeSessions(broker, logger));

        _activeClients = activeClients ?? ActiveClientOrchestration.CreateDefault(
            activeRuntime,
            options.Value,
            logger,
            broker);
    }

    internal IReadOnlyCollection<ConnectedClient> Clients => _sessions.Clients;

    // MARK: API
    // ========================================================================

    /// <summary>Adds the local server.</summary>
    public static IServiceCollection AddServices(IServiceCollection services)
    {
        _ = services.AddSingleton<ActiveClientRegistry>();
        _ = services.AddSingleton<IControllerOutputFactory, NoopControllerOutputFactory>();
        _ = services.AddSingleton<ControllerBroker>();
        _ = services.AddSingleton(static services => new VirtualMouseServer(
            services.GetRequiredService<IOptions<HostingSettings>>(),
            services.GetRequiredService<ILogger<VirtualMouseServer>>(),
            services.GetService<SettingsFile>(),
            services.GetService<ProfilesService>(),
            services.GetRequiredService<ActiveClientRegistry>(),
            activeClients: null,
            services.GetRequiredService<ControllerBroker>()));
        return services;
    }

    /// <summary>Runs the server until cancellation.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        string pipeName = _options.Value.PipeName;
        _logger.LogInformation("Listening on server pipe {PipeName}", pipeName);

        if (_settingsFile is not null)
        {
            _logger.LogInformation("Using settings {SettingsPath}", _settingsFile.Path);
        }

        using CancellationTokenSource orchestrationStop =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task orchestrationTask = _activeClients.RunAsync(orchestrationStop.Token);

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

                try
                {
                    await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                    NamedPipeServerStream connectedPipe = pipe;
                    ServerConnection connection = new(connectedPipe, _sessions);
                    _connections[connection] = 0;
                    _ = Task.Run(() => RunConnectionAsync(connection, cancellationToken), CancellationToken.None);
                    pipe = null;
                }
                finally
                {
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
            await DisposeConnectionsAsync().ConfigureAwait(false);
        }
    }

    // MARK: Helpers
    // ========================================================================

    private async Task RunConnectionAsync(ServerConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            await connection.RunAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ = _connections.TryRemove(connection, out _);
        }
    }

    private async Task DisposeConnectionsAsync()
    {
        foreach (ServerConnection connection in _connections.Keys)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
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

    private static IOptions<HostingSettings> RequireOptions(IOptions<HostingSettings> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options;
    }
}
