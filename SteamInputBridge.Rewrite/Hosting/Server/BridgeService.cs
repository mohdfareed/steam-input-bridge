using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SteamInputBridge.Settings;

namespace SteamInputBridge.Hosting.Server;

/// <summary>Owns server behavior and connected clients.</summary>
public sealed class BridgeService(SettingsService settings)
{
    private readonly Lock _gate = new();
    private readonly ConcurrentDictionary<Guid, ConnectedClient> _clients = [];

    /// <summary>Raised after the server status snapshot changes.</summary>
    public event EventHandler? StatusChanged;

    /// <summary>Current server status snapshot.</summary>
    public BridgeServerStatus Status => ServerStatus();

    /// <summary>Asks the connected client to exit.</summary>
    public async Task StopClientAsync(Guid connectionId)
    {
        ConnectedClient client;
        lock (_gate)
        {
            if (!_clients.TryGetValue(connectionId, out ConnectedClient? connectedClient))
            {
                throw new InvalidOperationException($"Client connection '{connectionId}' is not connected.");
            }

            client = connectedClient;
        }

        await client.Control.StopAsync().ConfigureAwait(false);
    }

    internal ConnectedClient RegisterClient(
        Guid connectionId,
        int processId,
        string profileId,
        uint? steamAppId,
        IBridgeClientApi control)
    {
        if (!settings.Current.Games.ContainsKey(profileId))
        {
            throw new InvalidOperationException($"Profile '{profileId}' is not configured.");
        }

        ConnectedClient client;
        lock (_gate)
        {
            if (_clients.ContainsKey(connectionId))
            {
                throw new InvalidOperationException($"Control connection '{connectionId}' is already registered.");
            }

            foreach (ConnectedClient existing in _clients.Values)
            {
                if (string.Equals(existing.ProfileId, profileId, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Profile '{profileId}' already has a connected client.");
                }
            }

            client = new(connectionId, processId, profileId, steamAppId, control);
            if (!_clients.TryAdd(connectionId, client))
            {
                throw new InvalidOperationException($"Control connection '{connectionId}' is already registered.");
            }
        }

        StatusChanged?.Invoke(this, EventArgs.Empty);
        return client;
    }

    internal ConnectedClient? UnregisterClient(Guid connectionId)
    {
        ConnectedClient? client;
        lock (_gate)
        {
            if (!_clients.TryRemove(connectionId, out client))
            {
                return null;
            }
        }

        StatusChanged?.Invoke(this, EventArgs.Empty);
        return client;
    }

    private BridgeServerStatus ServerStatus()
    {
        lock (_gate)
        {
            List<BridgeClientStatus> clients = new(_clients.Count);
            foreach (ConnectedClient client in _clients.Values)
            {
                clients.Add(new BridgeClientStatus(
                    client.ConnectionId,
                    client.ProcessId,
                    client.ProfileId,
                    client.SteamAppId));
            }

            return new BridgeServerStatus(clients);
        }
    }
}

internal sealed record ConnectedClient(
    Guid ConnectionId,
    int ProcessId,
    string ProfileId,
    uint? SteamAppId,
    IBridgeClientApi Control);
