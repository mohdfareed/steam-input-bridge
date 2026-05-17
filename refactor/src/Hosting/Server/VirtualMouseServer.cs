using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualMouse.Runtime;
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
        _ = services.AddSingleton<ActiveClientRegistry>();
        _ = services.AddSingleton<VirtualMouseServer>();
        return services;
    }
}

// MARK: Implementation
// ============================================================================

/// <summary>Current server status.</summary>
public sealed record ServerStatus(int ConnectedClientCount)
{
    /// <summary>Current active-client runtime status.</summary>
    public ActiveClientRegistryStatus Runtime { get; init; } =
        new(null, [], []);
}

// The app-facing server owns connected clients and accepts client pipes.
/// <summary>Long-lived local server for client connections.</summary>
public sealed class VirtualMouseServer(
    IOptions<HostingSettings> options,
    ILogger<VirtualMouseServer> logger,
    SettingsFile? settingsFile = null,
    ProfilesService? profiles = null,
    ActiveClientRegistry? runtime = null)
{
    private readonly ConnectedClients _clients = new();
    private readonly ConcurrentDictionary<ServerConnection, byte> _connections = [];
    private readonly ActiveClientRegistry _runtime = runtime ?? new ActiveClientRegistry();

    internal IReadOnlyCollection<ConnectedClient> Clients => _clients.Snapshot;

    // MARK: API
    // ========================================================================

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
                    ServerConnection connection = new(connectedPipe, this);
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

    // MARK: Server API
    // ========================================================================

    internal Guid ConnectClient(int processId)
    {
        ConnectedClient client = _clients.Add(processId);
        logger.LogInformation(
            "Client connected: {ClientId} process={ProcessId} (clients={ClientCount})",
            client.Id,
            client.ProcessId,
            _clients.Count);
        return client.Id;
    }

    internal void DisconnectClient(Guid clientId)
    {
        _clients.Remove(clientId);
        _runtime.RemoveClient(clientId);
        logger.LogInformation(
            "Client disconnected: {ClientId} (clients={ClientCount})",
            clientId,
            _clients.Count);
    }

    internal Task<ServerStatus> GetStatusAsync()
    {
        return Task.FromResult(new ServerStatus(_clients.Count)
        {
            Runtime = _runtime.GetStatus(),
        });
    }

    internal Task<ClientRunLaunch> StartRunAsync(Guid clientId, string profileId)
    {
        if (profiles is null)
        {
            throw new InvalidOperationException("Profile settings are not available.");
        }

        GameProfile profile = profiles.GetProfile(profileId) ??
            throw new InvalidOperationException($"Profile \"{profileId}\" was not found.");
        ResolvedGameProfile resolved = ProfileResolver.Resolve(profileId, profile);
        ConnectedClient client = _clients.Get(clientId);
        _runtime.RegisterClient(
            clientId,
            client.ProcessId,
            resolved.Id,
            resolved.ReceiverProcesses);

        return Task.FromResult(new ClientRunLaunch(
            resolved.Id,
            resolved.Title,
            resolved.Executable,
            resolved.Arguments,
            resolved.WorkingDirectory,
            resolved.ReceiverProcesses,
            resolved.ControllerOutput,
            resolved.MouseOutput));
    }

    internal Task UpdateRunProcessesAsync(
        Guid clientId,
        IReadOnlyList<ObservedGameProcess> processes)
    {
        _runtime.UpdateClientProcesses(clientId, processes);
        return Task.CompletedTask;
    }

    internal Task<IReadOnlyList<ObservedGameProcess>> GetOwnedReceiverProcessesAsync(Guid clientId)
    {
        return Task.FromResult(_runtime.GetOwnedProcesses(clientId));
    }

    internal Task EndRunAsync(Guid clientId)
    {
        _runtime.RemoveClient(clientId);
        return Task.CompletedTask;
    }

    internal void ConnectionClosed(Exception exception)
    {
        if (exception is not OperationCanceledException)
        {
            logger.LogInformation("Client pipe closed: {Message}", exception.Message);
        }
    }
}
