using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace VirtualMouse.Server;

// MARK: Connected Clients
// ============================================================================

public sealed record ConnectedClient(Guid Id, int ProcessId, DateTimeOffset ConnectedAt);

internal sealed class ConnectedClients
{
    private readonly ConcurrentDictionary<Guid, ConnectedClient> _clients = [];

    public IReadOnlyCollection<ConnectedClient> Snapshot => [.. _clients.Values];

    public int Count => _clients.Count;

    public ConnectedClient Add(int processId)
    {
        ConnectedClient client = new(Guid.NewGuid(), processId, DateTimeOffset.UtcNow);
        _clients[client.Id] = client;
        return client;
    }

    public void Remove(Guid clientId)
    {
        _ = _clients.TryRemove(clientId, out _);
    }
}
