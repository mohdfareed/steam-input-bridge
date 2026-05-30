using System;
using System.Collections.Concurrent;
using SteamInputBridge.Settings;

namespace SteamInputBridge.Hosting.Server;

/// <summary>Owns server behavior and connected clients.</summary>
public sealed class BridgeService(SettingsService settings)
{
    private readonly ConcurrentDictionary<Guid, ConnectedClient> _clients = [];

    /// <summary>Current server status snapshot.</summary>
    public BridgeServerStatus Status => new(_clients.Count);

    internal ConnectedClient RegisterClient(Guid connectionId, int processId, string profileId)
    {
        if (!settings.Current.Games.ContainsKey(profileId))
        {
            throw new InvalidOperationException($"Profile '{profileId}' is not configured.");
        }

        ConnectedClient client = new(connectionId, processId, profileId);
        return _clients.TryAdd(connectionId, client)
            ? client
            : throw new InvalidOperationException($"Control connection '{connectionId}' is already registered.");
    }

    internal ConnectedClient? UnregisterClient(Guid connectionId)
    {
        return _clients.TryRemove(connectionId, out ConnectedClient? client)
            ? client
            : null;
    }
}

internal sealed record ConnectedClient(Guid ConnectionId, int ProcessId, string ProfileId);
