using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SteamInputBridge.Forwarding.Mouse;
using SteamInputBridge.Outputs.Teensy;
using SteamInputBridge.Profiles;
using SteamInputBridge.Shortcuts;
using SteamInputBridge.Steam;

namespace SteamInputBridge.Hosting.Server;

/// <summary>Control API facade over server runtime services.</summary>
public sealed class BridgeService
{
    private static readonly TimeSpan ClientStatusTimeout = TimeSpan.FromMilliseconds(250);
    private readonly ShortcutService _shortcuts;
    private readonly ProfileClientsService _clients;
    private readonly ActiveProfileService _profiles;
    private readonly ServerMouseForwardingService _mouse;
    private readonly TeensyMouseOutputService _teensy;
    private readonly SteamInputConfigService _steamInput;

    /// <summary>Creates the control API facade.</summary>
    public BridgeService(
        ShortcutService shortcuts,
        ProfileClientsService clients,
        ActiveProfileService profiles,
        ServerMouseForwardingService mouse,
        TeensyMouseOutputService teensy,
        SteamInputConfigService steamInput)
    {
        _shortcuts = shortcuts;
        _clients = clients;
        _profiles = profiles;
        _mouse = mouse;
        _teensy = teensy;
        _steamInput = steamInput;
        _teensy.StatusChanged += OnTeensyStatusChanged;
    }

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
        await _clients.StopClientAsync(connectionId).ConfigureAwait(false);
    }

    /// <summary>Stops the connected profile session receiver processes.</summary>
    public void StopReceivers(Guid connectionId)
    {
        _clients.StopReceivers(connectionId);
    }

    internal async Task<ProfileClientStatus> RegisterClientAsync(
        Guid connectionId,
        int processId,
        string profileId,
        uint? steamAppId,
        IBridgeClientApi control)
    {
        ProfileClientStatus client = await _clients
            .ConnectClientAsync(connectionId, processId, profileId, steamAppId, control)
            .ConfigureAwait(false);
        StatusChanged?.Invoke(this, EventArgs.Empty);
        return client;
    }

    internal ProfileClientStatus? UnregisterClient(Guid connectionId, bool stopReceivers = true)
    {
        ProfileClientStatus? client = _clients.DisconnectClient(connectionId, stopReceivers);
        if (client is not null)
        {
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }

        return client;
    }

    private async Task<BridgeServerStatus> ServerStatusAsync()
    {
        IReadOnlyList<ProfileClientStatus> profileClients = _clients.Clients;
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

        IReadOnlyList<ProfileStatus> profileStatuses = _profiles.Profiles;
        return new BridgeServerStatus(
            ProfileStatuses(profileStatuses),
            clientStatuses,
            _shortcuts.Status,
            _mouse.Status,
            TeensyStatus(_teensy.Status),
            ControllerStatus(profileStatuses, clientStatuses),
            _steamInput.Status);
    }

    private async Task<Dictionary<Guid, BridgeClientRuntimeStatus>> ClientRuntimeStatusesAsync()
    {
        IReadOnlyList<ProfileClientsService.BridgeClientConnection> connections = _clients.Connections;
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

    private void OnTeensyStatusChanged(object? sender, EventArgs args)
    {
        _ = sender;
        _ = args;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private static List<BridgeProfileStatus> ProfileStatuses(IReadOnlyList<ProfileStatus> profiles)
    {
        List<BridgeProfileStatus> statuses = new(profiles.Count);
        foreach (ProfileStatus profile in profiles)
        {
            statuses.Add(new(
                profile.Id,
                profile.Definition,
                profile.Active,
                profile.ClientProcessId,
                profile.EffectiveSteamAppId,
                profile.GameProcessIds));
        }

        return statuses;
    }

    private static BridgeTeensyStatus TeensyStatus(TeensyOutputStatus status)
    {
        return new(status.State.ToString(), status.ConfiguredPort, status.ConnectedPort);
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
