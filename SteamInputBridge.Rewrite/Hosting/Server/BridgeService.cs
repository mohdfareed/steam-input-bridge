using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SteamInputBridge.Profiles;
using SteamInputBridge.Shortcuts;

namespace SteamInputBridge.Hosting.Server;

/// <summary>Control API facade over server runtime services.</summary>
public sealed class BridgeService(
    ShortcutService shortcuts,
    ProfilesService profiles)
{
    /// <summary>Raised after the server status snapshot changes.</summary>
    public event EventHandler? StatusChanged;

    /// <summary>Current server status snapshot.</summary>
    public BridgeServerStatus Status => ServerStatus();

    /// <summary>Stops the connected profile session and asks the client to exit.</summary>
    public async Task StopClientAsync(Guid connectionId)
    {
        await profiles.StopClientAsync(connectionId).ConfigureAwait(false);
    }

    /// <summary>Stops the connected profile session receiver processes.</summary>
    public void StopReceivers(Guid connectionId)
    {
        profiles.StopReceivers(connectionId);
    }

    internal ProfileClientStatus RegisterClient(
        Guid connectionId,
        int processId,
        string profileId,
        uint? steamAppId,
        IBridgeClientApi control)
    {
        ProfileClientStatus client = profiles.ConnectClient(connectionId, processId, profileId, steamAppId, control);
        StatusChanged?.Invoke(this, EventArgs.Empty);
        return client;
    }

    internal ProfileClientStatus? UnregisterClient(Guid connectionId)
    {
        ProfileClientStatus? client = profiles.DisconnectClient(connectionId);
        if (client is not null)
        {
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }

        return client;
    }

    private BridgeServerStatus ServerStatus()
    {
        IReadOnlyList<ProfileClientStatus> profileClients = profiles.Clients;
        List<BridgeClientStatus> clients = new(profileClients.Count);
        foreach (ProfileClientStatus client in profileClients)
        {
            clients.Add(new BridgeClientStatus(
                client.ConnectionId,
                client.ProcessId,
                client.ProfileId,
                client.SteamAppId));
        }

        return new BridgeServerStatus(clients, shortcuts.Status);
    }
}
