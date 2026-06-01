using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SteamInputBridge.Forwarding.Mouse;
using SteamInputBridge.Profiles;
using SteamInputBridge.Shortcuts;
using SteamInputBridge.Steam;

namespace SteamInputBridge.Hosting.Server;

/// <summary>Control API facade over server runtime services.</summary>
public sealed class BridgeService(
    ShortcutService shortcuts,
    ProfileClientsService clients,
    ActiveProfileService profiles,
    ServerMouseForwardingService mouse,
    SteamInputConfigService steamInput)
{
    private static readonly TimeSpan ClientStatusTimeout = TimeSpan.FromMilliseconds(250);

    /// <summary>Raised after the server status snapshot changes.</summary>
    public event EventHandler? StatusChanged;

    /// <summary>Current server status snapshot.</summary>
    public BridgeServerStatus Status => GetStatusAsync().GetAwaiter().GetResult();

    /// <summary>Gets current server status.</summary>
    public Task<BridgeServerStatus> GetStatusAsync()
    {
        return ServerStatusAsync();
    }

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

    private async Task<BridgeServerStatus> ServerStatusAsync()
    {
        IReadOnlyList<ProfileClientStatus> profileClients = clients.Clients;
        Dictionary<Guid, BridgeClientRuntimeStatus> runtimeStatuses = await ClientRuntimeStatusesAsync().ConfigureAwait(false);
        List<BridgeClientStatus> clientStatuses = new(profileClients.Count);
        foreach (ProfileClientStatus client in profileClients)
        {
            BridgeClientRuntimeStatus runtimeStatus = runtimeStatuses.GetValueOrDefault(client.ConnectionId) ??
                new BridgeClientRuntimeStatus(new(active: false, steamControllers: 0, virtualControllers: 0));
            clientStatuses.Add(new BridgeClientStatus(
                client.ConnectionId,
                client.ProcessId,
                client.ProfileId,
                client.SteamAppId,
                client.ReceiverProcessIds,
                runtimeStatus.Controller));
        }

        IReadOnlyList<ProfileStatus> profileStatuses = profiles.Profiles;
        return new BridgeServerStatus(
            ProfileStatuses(profileStatuses),
            clientStatuses,
            shortcuts.Status,
            mouse.Status,
            ControllerStatus(profileStatuses, clientStatuses),
            steamInput.Status);
    }

    private async Task<Dictionary<Guid, BridgeClientRuntimeStatus>> ClientRuntimeStatusesAsync()
    {
        IReadOnlyList<ProfileClientsService.BridgeClientConnection> connections = clients.Connections;
        Dictionary<Guid, BridgeClientRuntimeStatus> statuses = new(connections.Count);
        foreach (ProfileClientsService.BridgeClientConnection connection in connections)
        {
            try
            {
                BridgeClientRuntimeStatus status = await connection.Control
                    .GetStatusAsync()
                    .WaitAsync(ClientStatusTimeout)
                    .ConfigureAwait(false);
                statuses[connection.ConnectionId] = status;
            }
            catch (Exception exception) when (
                exception is TimeoutException or InvalidOperationException or ObjectDisposedException)
            {
            }
        }

        return statuses;
    }

    private static List<BridgeProfileStatus> ProfileStatuses(IReadOnlyList<ProfileStatus> profiles)
    {
        List<BridgeProfileStatus> statuses = new(profiles.Count);
        foreach (ProfileStatus profile in profiles)
        {
            statuses.Add(new(
                profile.Id,
                profile.Title,
                profile.Active,
                profile.ClientProcessId,
                profile.EffectiveSteamAppId,
                profile.MouseOutput?.ToString() ?? "None",
                profile.ControllerOutput?.ToString() ?? "None",
                profile.GameProcessIds));
        }

        return statuses;
    }

    private static BridgeControllerStatus ControllerStatus(
        IReadOnlyList<ProfileStatus> profiles,
        List<BridgeClientStatus> clients)
    {
        string? activeProfileId = null;
        foreach (ProfileStatus profile in profiles)
        {
            if (profile.Active)
            {
                activeProfileId = profile.Id;
                break;
            }
        }

        BridgeClientStatus? activeClient = null;
        int steamControllers = 0;
        int virtualControllers = 0;
        foreach (BridgeClientStatus client in clients)
        {
            steamControllers += client.Controller.SteamControllers;
            virtualControllers += client.Controller.VirtualControllers;
            if (activeProfileId is not null &&
                string.Equals(client.ProfileId, activeProfileId, StringComparison.OrdinalIgnoreCase))
            {
                activeClient = client;
            }
        }

        string clientState = clients.Count == 0
            ? "None"
            : activeClient is null ? "Inactive" : "Active";
        bool forwarding = activeClient?.Controller.Active == true &&
            activeClient.Controller.SteamControllers > 0 &&
            activeClient.Controller.VirtualControllers > 0;

        return new(clientState, steamControllers, virtualControllers, forwarding);
    }
}
