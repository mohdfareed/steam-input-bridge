using System;
using System.Collections.Generic;
using System.Linq;

namespace SteamInputBridge.Runtime;

public sealed partial class ActiveClientRegistry
{
    /// <summary>Gets current runtime status.</summary>
    public ActiveClientRegistryStatus GetStatus()
    {
        lock (_lock)
        {
            return new ActiveClientRegistryStatus(
                _foregroundProcessId,
                _activeClientId,
                [.. _clients.Values.Select(ToStatus)],
                [.. _claims.Select(ToClaimStatus)]);
        }
    }

    private ClientStatus ToStatus(ClientState client)
    {
        ObservedGameProcess[] owned =
            [.. client.Processes.Values.Where(process => OwnsProcess(client.ClientId, process.ProcessId))];
        ObservedGameProcess[] blocked =
            [.. client.Processes.Values.Where(process => !OwnsProcess(client.ClientId, process.ProcessId))];

        return new ClientStatus(
            client.ClientId,
            client.ClientProcessId,
            client.ProfileId,
            client.SteamAppId,
            _activeClientId == client.ClientId,
            client.ReceiverProcesses,
            [.. client.Processes.Values],
            owned,
            blocked);
    }

    private ReceiverProcessClaimStatus ToClaimStatus(KeyValuePair<int, List<Guid>> claim)
    {
        Guid ownerClientId = claim.Value[0];
        string processName = _clients.TryGetValue(ownerClientId, out ClientState? client) &&
            client.Processes.TryGetValue(claim.Key, out ObservedGameProcess? process)
            ? process.ProcessName
            : string.Empty;

        return new ReceiverProcessClaimStatus(
            claim.Key,
            processName,
            ownerClientId,
            [.. claim.Value]);
    }
}
