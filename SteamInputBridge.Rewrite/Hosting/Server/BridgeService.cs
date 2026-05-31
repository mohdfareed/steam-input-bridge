using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SteamInputBridge.Profiles;
using SteamInputBridge.Shortcuts;

namespace SteamInputBridge.Hosting.Server;

/// <summary>Control API facade over server runtime services.</summary>
public sealed class BridgeService(ShortcutService shortcuts, ProfileClientsService clients)
{
    /// <summary>Raised after the server status snapshot changes.</summary>
    public event EventHandler? StatusChanged;

    /// <summary>Current server status snapshot.</summary>
    public BridgeServerStatus Status => ServerStatus();

    /// <summary>Stops the connected profile session and asks the client to exit.</summary>
    public async Task StopClientAsync(Guid connectionId)
    {
        await clients.StopClientAsync(connectionId).ConfigureAwait(false);
    }

    /// <summary>Stops the connected profile session receiver processes.</summary>
    public void StopReceivers(Guid connectionId)
    {
        clients.StopReceivers(connectionId);
    }

    internal async Task<ProfileClientStatus> RegisterClientAsync(
        Guid connectionId,
        int processId,
        string profileId,
        uint? steamAppId,
        IBridgeClientApi control)
    {
        ProfileClientStatus client = await clients
            .ConnectClientAsync(connectionId, processId, profileId, steamAppId, control)
            .ConfigureAwait(false);
        StatusChanged?.Invoke(this, EventArgs.Empty);
        return client;
    }

    internal ProfileClientStatus? UnregisterClient(Guid connectionId)
    {
        ProfileClientStatus? client = clients.DisconnectClient(connectionId);
        if (client is not null)
        {
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }

        return client;
    }

    private BridgeServerStatus ServerStatus()
    {
        IReadOnlyList<ProfileClientStatus> profileClients = clients.Clients;
        List<BridgeClientStatus> clientStatuses = new(profileClients.Count);
        foreach (ProfileClientStatus client in profileClients)
        {
            clientStatuses.Add(new BridgeClientStatus(
                client.ConnectionId,
                client.ProcessId,
                client.ProfileId,
                client.SteamAppId));
        }

        return new BridgeServerStatus(clientStatuses, shortcuts.Status);
    }
}
