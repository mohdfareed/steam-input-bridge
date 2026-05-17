using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace VirtualMouse.Runtime;

internal sealed class ClientState(
    Guid clientId,
    int clientProcessId,
    string profileId,
    IReadOnlyList<string> receiverProcesses)
{
    public Guid ClientId { get; } = clientId;

    public int ClientProcessId { get; } = clientProcessId;

    public string ProfileId { get; } = profileId;

    public IReadOnlyList<string> ReceiverProcesses { get; } = receiverProcesses;

    public Dictionary<int, ObservedGameProcess> Processes { get; } = [];
}

/// <summary>Tracks receiver-process ownership and the active client.</summary>
public sealed class ActiveClientRegistry
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, ClientState> _clients = [];
    private readonly Dictionary<int, List<Guid>> _claims = [];
    private int _foregroundProcessId;
    private Guid? _activeClientId;

    // MARK: API
    // ========================================================================

    /// <summary>Raised when the active client changes.</summary>
    public event EventHandler<ActiveClientChangedEventArgs>? ActiveClientChanged;

    /// <summary>Registers one connected client.</summary>
    public void RegisterClient(
        Guid clientId,
        int clientProcessId,
        string profileId,
        IReadOnlyList<string> receiverProcesses)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentNullException.ThrowIfNull(receiverProcesses);

        ClientState client = new(clientId, clientProcessId, profileId, receiverProcesses);
        lock (_gate)
        {
            _clients[clientId] = client;
        }
    }

    /// <summary>Replaces the receiver process snapshot for a client.</summary>
    public void UpdateClientProcesses(
        Guid clientId,
        IReadOnlyList<ObservedGameProcess> processes)
    {
        ArgumentNullException.ThrowIfNull(processes);

        ActiveClientChangedEventArgs? changed;
        lock (_gate)
        {
            ClientState client = GetClient(clientId);
            Dictionary<int, ObservedGameProcess> next = FilterProcesses(client, processes);
            foreach (int removedProcessId in client.Processes.Keys.Except(next.Keys).ToArray())
            {
                _ = client.Processes.Remove(removedProcessId);
                RemoveObserver(clientId, removedProcessId);
            }

            foreach (ObservedGameProcess process in next.Values)
            {
                client.Processes[process.ProcessId] = process;
                Observe(clientId, process.ProcessId);
            }

            changed = RefreshActiveClient();
        }

        RaiseChanged(changed);
    }

    /// <summary>Removes a connected client and releases its receiver-process claims.</summary>
    public void RemoveClient(Guid clientId)
    {
        ActiveClientChangedEventArgs? changed;
        lock (_gate)
        {
            if (!_clients.Remove(clientId, out ClientState? client))
            {
                return;
            }

            foreach (int processId in client.Processes.Keys.ToArray())
            {
                RemoveObserver(clientId, processId);
            }

            changed = RefreshActiveClient();
        }

        RaiseChanged(changed);
    }

    /// <summary>Refreshes active-client state from the foreground process id.</summary>
    public void RefreshActiveClient(int foregroundProcessId)
    {
        ActiveClientChangedEventArgs? changed;
        lock (_gate)
        {
            _foregroundProcessId = foregroundProcessId;
            changed = RefreshActiveClient();
        }

        RaiseChanged(changed);
    }

    /// <summary>Gets receiver processes currently owned by one client.</summary>
    public IReadOnlyList<ObservedGameProcess> GetOwnedProcesses(Guid clientId)
    {
        lock (_gate)
        {
            ClientState client = GetClient(clientId);
            return [.. client.Processes.Values.Where(process => Owns(clientId, process.ProcessId))];
        }
    }

    /// <summary>Gets current runtime status.</summary>
    public ActiveClientRegistryStatus GetStatus()
    {
        lock (_gate)
        {
            return new ActiveClientRegistryStatus(
                _activeClientId,
                [.. _clients.Values.Select(ToStatus)],
                [.. _claims.Select(ToClaimStatus)]);
        }
    }

    // MARK: Helpers
    // ========================================================================

    private void Observe(Guid clientId, int processId)
    {
        if (!_claims.TryGetValue(processId, out List<Guid>? observers))
        {
            observers = [];
            _claims[processId] = observers;
        }

        if (!observers.Contains(clientId))
        {
            observers.Add(clientId);
        }
    }

    private void RemoveObserver(Guid clientId, int processId)
    {
        if (_claims.TryGetValue(processId, out List<Guid>? observers))
        {
            _ = observers.Remove(clientId);
            if (observers.Count == 0)
            {
                _ = _claims.Remove(processId);
            }
        }
    }

    private ActiveClientChangedEventArgs? RefreshActiveClient()
    {
        Guid? previous = _activeClientId;
        Guid? current = _foregroundProcessId > 0 && _claims.TryGetValue(_foregroundProcessId, out List<Guid>? observers)
            ? observers[0]
            : null;

        if (previous == current)
        {
            return null;
        }

        _activeClientId = current;
        return new ActiveClientChangedEventArgs(previous, current);
    }

    private ClientStatus ToStatus(ClientState client)
    {
        ObservedGameProcess[] owned =
            [.. client.Processes.Values.Where(process => Owns(client.ClientId, process.ProcessId))];
        ObservedGameProcess[] blocked =
            [.. client.Processes.Values.Where(process => !Owns(client.ClientId, process.ProcessId))];

        return new ClientStatus(
            client.ClientId,
            client.ClientProcessId,
            client.ProfileId,
            _activeClientId == client.ClientId,
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

    private bool Owns(Guid clientId, int processId)
    {
        return _claims.TryGetValue(processId, out List<Guid>? observers) &&
            observers.Count > 0 &&
            observers[0] == clientId;
    }

    private ClientState GetClient(Guid clientId)
    {
        return _clients.TryGetValue(clientId, out ClientState? client)
            ? client
            : throw new InvalidOperationException($"Client {clientId} is not active.");
    }

    private static Dictionary<int, ObservedGameProcess> FilterProcesses(
        ClientState client,
        IReadOnlyList<ObservedGameProcess> processes)
    {
        Dictionary<int, ObservedGameProcess> filtered = [];
        foreach (ObservedGameProcess process in processes)
        {
            if (process.ProcessId > 0 &&
                client.ReceiverProcesses.Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase))
            {
                filtered[process.ProcessId] = process;
            }
        }

        return filtered;
    }

    private void RaiseChanged(ActiveClientChangedEventArgs? changed)
    {
        if (changed is not null)
        {
            ActiveClientChanged?.Invoke(this, changed);
        }
    }
}
