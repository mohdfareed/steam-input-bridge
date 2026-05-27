using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Forwarding.Controller.Routing;
using SteamInputBridge.Forwarding.Mouse;
using SteamInputBridge.Hosting.Server.Pipes;
using SteamInputBridge.Runtime;
using SteamInputBridge.Settings.Profiles;

namespace SteamInputBridge.Hosting.Server.Orchestration;

internal sealed record ConnectedClient(Guid Id, int ProcessId, DateTimeOffset ConnectedAt);

internal sealed partial class ServerSessions(
    ILogger logger,
    ProfilesService? profiles,
    ActiveClientRegistry runtime,
    ControllerBroker forwarding,
    MouseBroker mouseForwarding,
    ControllerPipeSessions controllerPipes,
    Func<ServerInputStatus>? getInputStatus = null,
    Func<ServerSteamInputStatus>? getSteamInputStatus = null,
    Func<ServerHidHideStatus>? getHidHideStatus = null,
    Func<OverlayStatus>? getOverlayStatus = null,
    Action? routeStateChanged = null,
    Action? statusChanged = null)
{
    private readonly ConcurrentDictionary<Guid, ConnectedClient> _clients = [];

    internal IReadOnlyCollection<ConnectedClient> Clients => [.. _clients.Values];

    internal Guid ConnectClient(int processId)
    {
        ConnectedClient client = new(Guid.NewGuid(), processId, DateTimeOffset.UtcNow);
        _clients[client.Id] = client;
        HostingLog.ClientConnected(logger, client.Id, client.ProcessId, _clients.Count);
        statusChanged?.Invoke();
        return client.Id;
    }

    internal async Task DisconnectClientAsync(Guid clientId)
    {
        _ = _clients.TryRemove(clientId, out _);
        runtime.RemoveClient(clientId);
        forwarding.RemoveClient(clientId);
        mouseForwarding.RemoveClient(clientId);
        await controllerPipes.RemoveAsync(clientId).ConfigureAwait(false);
        routeStateChanged?.Invoke();
        statusChanged?.Invoke();

        HostingLog.ClientDisconnected(logger, clientId, _clients.Count);
    }

    internal async Task EndRunAsync(Guid clientId)
    {
        runtime.RemoveClient(clientId);
        forwarding.RemoveClient(clientId);
        mouseForwarding.RemoveClient(clientId);
        await controllerPipes.RemoveAsync(clientId).ConfigureAwait(false);
        routeStateChanged?.Invoke();
        statusChanged?.Invoke();
    }

    internal async Task StopClientAsync(Guid clientId)
    {
        ConnectedClient client = GetClient(clientId);
        _ = GameProcessKiller.Kill(runtime.GetLifecycleOwnedProcesses(clientId));
        if (client.ProcessId != Environment.ProcessId)
        {
            _ = GameProcessKiller.KillProcess(client.ProcessId);
        }

        await EndRunAsync(clientId).ConfigureAwait(false);
    }

    internal void ConnectionClosed(Exception exception)
    {
        if (exception is not OperationCanceledException)
        {
            HostingLog.ClientPipeClosed(logger, exception.Message);
        }
    }

    private ConnectedClient GetClient(Guid clientId)
    {
        return _clients.TryGetValue(clientId, out ConnectedClient? client)
            ? client
            : throw new InvalidOperationException($"Client {clientId} is not connected.");
    }
}
