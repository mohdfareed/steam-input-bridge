using System;
using System.Collections.Generic;
using SteamInputBridge.Outputs;
using SteamInputBridge.Settings;

namespace SteamInputBridge.Profiles;

public sealed partial class ProfilesService
{
    // MARK: Resolution
    // ========================================================================

    private static Dictionary<string, ResolvedProfile> ResolveProfiles(SteamInputBridgeSettings settings)
    {
        Dictionary<string, ResolvedProfile> profiles = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string profileId, GameProfile profile) in settings.Games)
        {
            profiles[profileId] = new(
                profileId,
                profile.Title,
                profile.SteamAppId,
                profile.MouseOutput,
                profile.ControllerOutput,
                [.. profile.ReceiverProcesses]);
        }

        return profiles;
    }

    private static Dictionary<Guid, ConnectedProfileClient> ConnectedClientsForKnownProfiles(
        Dictionary<Guid, ConnectedProfileClient> clients,
        Dictionary<string, ResolvedProfile> profiles)
    {
        Dictionary<Guid, ConnectedProfileClient> knownClients = [];
        foreach ((Guid connectionId, ConnectedProfileClient client) in clients)
        {
            if (profiles.ContainsKey(client.ProfileId))
            {
                knownClients[connectionId] = client;
            }
        }

        return knownClients;
    }

    private ConnectedProfileClient? ConnectedClient(string profileId)
    {
        foreach (ConnectedProfileClient client in _clients.Values)
        {
            if (string.Equals(client.ProfileId, profileId, StringComparison.OrdinalIgnoreCase))
            {
                return client;
            }
        }

        return null;
    }

    private string? ActiveProfileIdLocked()
    {
        return _activeProfile?.Id;
    }

    private sealed record ResolvedProfile(
        string Id,
        string Title,
        uint? SteamAppId,
        MouseOutput? MouseOutput,
        ControllerOutput? ControllerOutput,
        IReadOnlyList<string> ReceiverProcesses);

    private sealed record ConnectedProfileClient(Guid ConnectionId, int ProcessId, string ProfileId, uint? SteamAppId);
}
