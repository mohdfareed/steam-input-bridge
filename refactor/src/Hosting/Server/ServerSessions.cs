using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VirtualMouse.Runtime;
using VirtualMouse.Settings.Profiles;

namespace VirtualMouse.Hosting;

internal sealed record ConnectedClient(Guid Id, int ProcessId, DateTimeOffset ConnectedAt);

internal sealed class ServerSessions(
    ILogger logger,
    ProfilesService? profiles,
    ActiveClientRegistry runtime)
{
    private readonly ConcurrentDictionary<Guid, ConnectedClient> _clients = [];

    internal IReadOnlyCollection<ConnectedClient> Clients => [.. _clients.Values];

    internal Guid ConnectClient(int processId)
    {
        ConnectedClient client = new(Guid.NewGuid(), processId, DateTimeOffset.UtcNow);
        _clients[client.Id] = client;
        logger.LogInformation(
            "Client connected: {ClientId} process={ProcessId} (clients={ClientCount})",
            client.Id,
            client.ProcessId,
            _clients.Count);
        return client.Id;
    }

    internal void DisconnectClient(Guid clientId)
    {
        _ = _clients.TryRemove(clientId, out _);
        runtime.RemoveClient(clientId);
        logger.LogInformation(
            "Client disconnected: {ClientId} (clients={ClientCount})",
            clientId,
            _clients.Count);
    }

    internal Task<ServerStatus> GetStatusAsync()
    {
        return Task.FromResult(new ServerStatus(_clients.Count)
        {
            Runtime = runtime.GetStatus(),
        });
    }

    internal Task<ClientRunLaunch> StartRunAsync(Guid clientId, StartRunRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (profiles is null)
        {
            throw new InvalidOperationException("Profile settings are not available.");
        }

        GameProfile profile = profiles.GetProfile(request.ProfileId) ??
            throw new InvalidOperationException($"Profile \"{request.ProfileId}\" was not found.");
        ResolvedGameProfile resolved = ProfileResolver.Resolve(request.ProfileId, profile);
        ConnectedClient client = GetClient(clientId);
        runtime.RegisterClient(
            clientId,
            client.ProcessId,
            resolved.Id,
            request.SteamAppId,
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
        runtime.UpdateClientProcesses(clientId, processes);
        return Task.CompletedTask;
    }

    internal Task<IReadOnlyList<ObservedGameProcess>> GetOwnedReceiverProcessesAsync(Guid clientId)
    {
        return Task.FromResult(runtime.GetOwnedProcesses(clientId));
    }

    internal Task EndRunAsync(Guid clientId)
    {
        runtime.RemoveClient(clientId);
        return Task.CompletedTask;
    }

    internal void ConnectionClosed(Exception exception)
    {
        if (exception is not OperationCanceledException)
        {
            logger.LogInformation("Client pipe closed: {Message}", exception.Message);
        }
    }

    private ConnectedClient GetClient(Guid clientId)
    {
        return _clients.TryGetValue(clientId, out ConnectedClient? client)
            ? client
            : throw new InvalidOperationException($"Client {clientId} is not connected.");
    }
}
