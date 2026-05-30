using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace SteamInputBridge.Runtime;

/// <summary>Tracks receiver-process ownership and the active client.</summary>
public sealed partial class ActiveClientRegistry
{
    private readonly Lock _lock = new();
    private readonly Dictionary<Guid, ClientState> _clients = [];
    private readonly Dictionary<int, List<Guid>> _claims = [];
    private int _foregroundProcessId;
    private Guid? _activeClientId;

    // MARK: Publics
    // ========================================================================

    /// <summary>Raised when the active client changes.</summary>
    public event EventHandler<ActiveClientChangedEventArgs>? ActiveClientChanged;

    /// <summary>Registers one connected client.</summary>
    public void RegisterClient(
        Guid clientId,
        int clientProcessId,
        string profileId,
        uint? steamAppId,
        IReadOnlyList<string> receiverProcesses,
        bool ownsReceiverProcesses = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentNullException.ThrowIfNull(receiverProcesses);

        ClientState client = new(
            clientId,
            clientProcessId,
            profileId,
            steamAppId,
            receiverProcesses,
            ownsReceiverProcesses);
        lock (_lock)
        {
            _clients[clientId] = client;
        }
    }

    /// <summary>Removes a connected client and releases its receiver-process claims.</summary>
    public void RemoveClient(Guid clientId)
    {
        ActiveClientChangedEventArgs? changed;

        lock (_lock)
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

    /// <summary>Replaces the receiver process snapshot for a client.</summary>
    public void UpdateClient(Guid clientId, IReadOnlyList<ObservedGameProcess> processes)
    {
        ArgumentNullException.ThrowIfNull(processes);
        ActiveClientChangedEventArgs? changed;

        lock (_lock)
        {
            ClientState client = GetClient(clientId);
            Dictionary<int, ObservedGameProcess> added = FilterProcesses(client, processes);

            foreach (int removedProcessId in client.Processes.Keys.Except(added.Keys).ToArray())
            {
                _ = client.Processes.Remove(removedProcessId);
                RemoveObserver(clientId, removedProcessId);
            }

            foreach (ObservedGameProcess process in added.Values)
            {
                client.Processes[process.ProcessId] = process;
                AddObserver(clientId, process.ProcessId);
            }

            changed = RefreshActiveClient();
        }

        RaiseChanged(changed);
    }

    /// <summary>Refreshes active-client state from the foreground process id.</summary>
    public void RefreshClients(int foregroundProcessId)
    {
        ActiveClientChangedEventArgs? changed;
        lock (_lock)
        {
            _foregroundProcessId = foregroundProcessId;
            changed = RefreshActiveClient();
        }

        RaiseChanged(changed);
    }

    /// <summary>Gets receiver processes this client lifecycle owns and may stop.</summary>
    public IReadOnlyList<ObservedGameProcess> GetLifecycleOwnedProcesses(Guid clientId)
    {
        lock (_lock)
        {
            ClientState client = GetClient(clientId);
            return client.OwnsReceiverProcesses
                ? [.. client.Processes.Values.Where(process => OwnsProcess(clientId, process.ProcessId))]
                : [];
        }
    }

    // MARK: Privates
    // ========================================================================

    private ClientState GetClient(Guid clientId)
    {
        return _clients.TryGetValue(clientId, out ClientState? client)
            ? client
            : throw new InvalidOperationException($"Client {clientId} is not active.");
    }

    private void AddObserver(Guid clientId, int processId)
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
        Guid? current =
            _foregroundProcessId > 0 && _claims.TryGetValue(_foregroundProcessId, out List<Guid>? observers)
                ? observers[0]
                : null;

        if (previous == current)
        {
            return null;
        }

        _activeClientId = current;
        return new ActiveClientChangedEventArgs(previous, current);
    }

    private bool OwnsProcess(Guid clientId, int processId)
    {
        return _claims.TryGetValue(processId, out List<Guid>? observers) &&
            observers.Count > 0 &&
            observers[0] == clientId;
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
