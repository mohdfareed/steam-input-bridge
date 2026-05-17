using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualMouse.Settings;
using VirtualMouse.Settings.Profiles;

namespace VirtualMouse.Hosting;

// MARK: Dependency Injection
// ============================================================================

/// <summary>Dependency injection registration for the app-facing server.</summary>
public static class ServerServices
{
    /// <summary>Adds the local server.</summary>
    public static IServiceCollection AddApplicationServer(this IServiceCollection services)
    {
        _ = services.AddSingleton<VirtualMouseServer>();
        return services;
    }
}

// The app-facing server owns connected clients and accepts client pipes.
/// <summary>Long-lived local server for client connections.</summary>
public sealed class VirtualMouseServer(
    IOptions<HostingSettings> options,
    ILogger<VirtualMouseServer> logger,
    SettingsFile? settingsFile = null,
    ProfilesService? profiles = null)
{
    private readonly ConnectedClients _clients = new();
    private readonly ConcurrentDictionary<ServerConnection, byte> _connections = [];

    internal IReadOnlyCollection<ConnectedClient> Clients => _clients.Snapshot;

    // MARK: API
    // ============================================================================

    /// <summary>Runs the server until cancellation.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        string pipeName = options.Value.PipeName;
        logger.LogInformation("Listening on server pipe {PipeName}", pipeName);

        if (settingsFile is not null)
        {
            logger.LogInformation("Using settings {SettingsPath}", settingsFile.Path);
        }

        if (profiles is not null)
        {
            logger.LogInformation("Profile settings enabled");
        }

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
                    ServerConnection connection = new(connectedPipe, _clients, logger);
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
            await DisposeConnectionsAsync().ConfigureAwait(false);
        }
    }

    // MARK: Helpers
    // ============================================================================

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
}
