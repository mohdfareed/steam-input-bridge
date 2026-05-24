using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SteamInputBridge.Forwarding.Controller.Routing;

// MARK: Models
// ========================================================================

/// <summary>Current controller forwarding status.</summary>
public sealed record ControllerBrokerStatus(
    Guid? ActiveClientId,
    bool ControllerOutputEnabled,
    bool PhysicalMotionEnabled,
    IReadOnlyList<ControllerSlotStatus> Slots);

/// <summary>Status for one physical controller slot.</summary>
public sealed record ControllerSlotStatus(
    ControllerId ControllerId,
    bool OutputConnected,
    ControllerOutput Output,
    bool HasActiveClientEndpoint,
    bool HasPhysicalEndpoint,
    int ClientEndpointCount,
    ControllerFeatures? PhysicalFeatures,
    ControllerFeatures? ActiveClientFeatures);

/// <summary>Registered client endpoint for one physical controller slot.</summary>
public sealed record ControllerClientRegistration(
    ushort ControllerIndex,
    ControllerId ControllerId,
    ControllerFeatures Features,
    bool CanOwnOutputWithoutPhysical = false);

/// <summary>Routes active-client controller input to game-facing controller outputs.</summary>
public sealed partial class ControllerBroker(IControllerOutputFactory outputFactory) : IDisposable, IAsyncDisposable
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, ClientEntry> _clients = [];
    private readonly Dictionary<ControllerId, ControllerSlot> _slots = [];
    private Guid? _activeClientId;
    private bool _controllerOutputEnabled = true;
    private bool _physicalMotionEnabled = true;
    private bool _disposed;

    // MARK: Publics
    // ========================================================================

    /// <summary>Registers a connected client and the output shape its profile wants.</summary>
    public void RegisterClient(Guid clientId, ControllerOutput controllerOutput)
    {
        ThrowIfDisposed();
        lock (_gate)
        {
            _clients[clientId] = new ClientEntry(controllerOutput);
            RefreshOutputs();
        }
    }

    /// <summary>Sets the active client whose controller streams may drive outputs.</summary>
    public void SetActiveClient(Guid? clientId)
    {
        ThrowIfDisposed();
        List<IControllerOutput> dispose = [];
        List<PendingControllerSend> sends = [];
        lock (_gate)
        {
            Guid? previousClientId = _activeClientId;
            Guid? nextClientId = clientId.HasValue && _clients.ContainsKey(clientId.Value)
                ? clientId
                : null;
            if (previousClientId != nextClientId && previousClientId.HasValue)
            {
                ClearClientStates(previousClientId.Value);
            }

            _activeClientId = clientId.HasValue && _clients.ContainsKey(clientId.Value)
                ? clientId
                : null;
            RefreshOutputs(dispose);
            RetargetFeedback();
            AddCurrentStateSends(sends);
        }

        SendOutputs(sends);
        DisposeOutputs(dispose);
    }

    /// <summary>Removes a client and releases endpoints owned by it.</summary>
    public void RemoveClient(Guid clientId)
    {
        ThrowIfDisposed();
        List<IControllerOutput> dispose = [];
        List<PendingControllerSend> sends = [];
        lock (_gate)
        {
            _ = _clients.Remove(clientId);
            if (_activeClientId == clientId)
            {
                _activeClientId = null;
            }

            foreach (ControllerSlot slot in _slots.Values)
            {
                slot.RemoveClient(clientId);
            }

            PruneEmptySlots(dispose);
            RefreshOutputs(dispose);
            RetargetFeedback();
            AddCurrentStateSends(sends);
        }

        SendOutputs(sends);
        DisposeOutputs(dispose);
    }

    /// <summary>Removes controller endpoints owned by a connected client.</summary>
    public void RemoveClientControllers(Guid clientId)
    {
        ThrowIfDisposed();
        List<IControllerOutput> dispose = [];
        List<PendingControllerSend> sends = [];
        lock (_gate)
        {
            foreach (ControllerSlot slot in _slots.Values)
            {
                slot.RemoveClient(clientId);
            }

            PruneEmptySlots(dispose);
            RefreshOutputs(dispose);
            RetargetFeedback();
            AddCurrentStateSends(sends);
        }

        SendOutputs(sends);
        DisposeOutputs(dispose);
    }

    /// <summary>Registers one client-visible controller stream before input frames arrive.</summary>
    public void RegisterClientController(
        Guid clientId,
        ushort controllerIndex,
        ControllerId physicalControllerId,
        ControllerFeatures features)
    {
        ThrowIfDisposed();
        PendingControllerSend? send;
        lock (_gate)
        {
            if (!_clients.ContainsKey(clientId))
            {
                return;
            }

            ControllerSlot slot = GetOrCreateSlot(physicalControllerId);
            ControllerEndpointId endpointId = new(clientId, controllerIndex);
            if (!slot.ClientEndpoints.ContainsKey(endpointId))
            {
                slot.ClientEndpoints[endpointId] =
                    new ControllerEndpointState(ControllerState.Empty, features, null);
            }

            RefreshOutput(slot);
            send = _activeClientId == clientId
                ? CreateCurrentStateSend(slot)
                : null;
        }

        SendOutput(send);
    }

    /// <summary>Replaces one client's registered controller streams as one atomic route update.</summary>
    public void SetClientControllers(
        Guid clientId,
        IReadOnlyList<ControllerClientRegistration> controllers)
    {
        ArgumentNullException.ThrowIfNull(controllers);
        ThrowIfDisposed();

        List<IControllerOutput> dispose = [];
        List<PendingControllerSend> sends = [];
        lock (_gate)
        {
            if (!_clients.ContainsKey(clientId))
            {
                return;
            }

            Dictionary<ControllerEndpointId, ControllerClientRegistration> next = [];
            foreach (ControllerClientRegistration controller in controllers)
            {
                next[new ControllerEndpointId(clientId, controller.ControllerIndex)] = controller;
            }

            foreach (ControllerSlot slot in _slots.Values)
            {
                foreach (ControllerEndpointId endpointId in slot.ClientEndpoints.Keys.ToArray())
                {
                    if (endpointId.ClientId != clientId)
                    {
                        continue;
                    }

                    if (!next.TryGetValue(endpointId, out ControllerClientRegistration? registration) ||
                        slot.ControllerId != registration.ControllerId)
                    {
                        slot.RemoveClientController(endpointId);
                    }
                }
            }

            foreach (KeyValuePair<ControllerEndpointId, ControllerClientRegistration> entry in next)
            {
                ControllerSlot slot = GetOrCreateSlot(entry.Value.ControllerId);
                ControllerEndpointState state =
                    slot.ClientEndpoints.TryGetValue(entry.Key, out ControllerEndpointState current)
                        ? new ControllerEndpointState(
                            current.State,
                            entry.Value.Features,
                            current.FeedbackSink,
                            entry.Value.CanOwnOutputWithoutPhysical)
                        : new ControllerEndpointState(
                            ControllerState.Empty,
                            entry.Value.Features,
                            null,
                            entry.Value.CanOwnOutputWithoutPhysical);
                slot.ClientEndpoints[entry.Key] = state;
            }

            PruneEmptySlots(dispose);
            RefreshOutputs(dispose);
            RetargetFeedback();
            AddCurrentStateSends(sends);
        }

        SendOutputs(sends);
        DisposeOutputs(dispose);
    }

    /// <summary>Removes one controller endpoint owned by a connected client.</summary>
    public void RemoveClientController(Guid clientId, ushort controllerIndex)
    {
        ThrowIfDisposed();
        ControllerEndpointId endpointId = new(clientId, controllerIndex);
        List<IControllerOutput> dispose = [];
        List<PendingControllerSend> sends = [];
        lock (_gate)
        {
            foreach (ControllerSlot slot in _slots.Values)
            {
                slot.RemoveClientController(endpointId);
            }

            PruneEmptySlots(dispose);
            RefreshOutputs(dispose);
            RetargetFeedback();
            AddCurrentStateSends(sends);
        }

        SendOutputs(sends);
        DisposeOutputs(dispose);
    }

    /// <summary>Updates a client-visible controller stream from one client.</summary>
    public void UpdateClientController(
        Guid clientId,
        ushort controllerIndex,
        ControllerId physicalControllerId,
        ControllerState state,
        ControllerFeatures features,
        IControllerFeedbackSink? feedbackSink = null)
    {
        ThrowIfDisposed();
        PendingControllerSend? send;

        lock (_gate)
        {
            if (!_clients.ContainsKey(clientId))
            {
                return;
            }

            // Inactive clients may keep streaming while another game is in
            // focus. Ignore those frames completely so they cannot become the
            // next active state's stale output.
            if (_activeClientId != clientId)
            {
                return;
            }

            ControllerSlot slot = GetOrCreateSlot(physicalControllerId);
            ControllerEndpointId endpointId = new(clientId, controllerIndex);
            bool hasCurrent = slot.ClientEndpoints.TryGetValue(endpointId, out ControllerEndpointState current);
            bool replayFeedback =
                !hasCurrent ||
                !ReferenceEquals(current.FeedbackSink, feedbackSink) ||
                current.Features != features;
            slot.ClientEndpoints[endpointId] = new ControllerEndpointState(
                state,
                features,
                feedbackSink,
                hasCurrent && current.CanOwnOutputWithoutPhysical);

            if (ShouldTryConnectOutput(slot))
            {
                RefreshOutput(slot);
            }

            send = _activeClientId == clientId
                ? CreateCurrentStateSend(slot)
                : null;
            if (_activeClientId == clientId && replayFeedback)
            {
                slot.ReplayFeedback(clientId);
            }
        }

        SendOutput(send);
    }

    /// <summary>Updates an already-registered client-visible controller stream.</summary>
    public void UpdateClientController(
        Guid clientId,
        ushort controllerIndex,
        ControllerState state,
        ControllerFeatures features,
        IControllerFeedbackSink? feedbackSink = null)
    {
        ThrowIfDisposed();
        PendingControllerSend? send;

        lock (_gate)
        {
            if (!_clients.ContainsKey(clientId) ||
                !TryFindSlot(new ControllerEndpointId(clientId, controllerIndex), out ControllerSlot slot))
            {
                return;
            }

            // Inactive clients may keep streaming while another game is in
            // focus. Ignore those frames completely so they cannot become the
            // next active state's stale output.
            if (_activeClientId != clientId)
            {
                return;
            }

            ControllerEndpointId endpointId = new(clientId, controllerIndex);
            ControllerEndpointState current = slot.ClientEndpoints[endpointId];
            bool replayFeedback =
                !ReferenceEquals(current.FeedbackSink, feedbackSink) ||
                current.Features != features;
            slot.ClientEndpoints[endpointId] = new ControllerEndpointState(
                state,
                features,
                feedbackSink,
                current.CanOwnOutputWithoutPhysical);

            send = _activeClientId == clientId
                ? CreateCurrentStateSend(slot)
                : null;
            if (_activeClientId == clientId && replayFeedback)
            {
                slot.ReplayFeedback(clientId);
            }
        }

        SendOutput(send);
    }

    /// <summary>Updates controller index zero from one client.</summary>
    public void UpdateClientController(
        Guid clientId,
        ControllerId physicalControllerId,
        ControllerState state,
        ControllerFeatures features,
        IControllerFeedbackSink? feedbackSink = null)
    {
        UpdateClientController(clientId, 0, physicalControllerId, state, features, feedbackSink);
    }

    /// <summary>Updates the latest physical controller state for one slot.</summary>
    public void UpdatePhysicalController(
        ControllerId controllerId,
        ControllerState state,
        ControllerFeatures features,
        IControllerFeedbackSink? feedbackSink = null)
    {
        ThrowIfDisposed();
        PendingControllerSend? send;

        lock (_gate)
        {
            ControllerSlot slot = GetOrCreateSlot(controllerId);
            bool replayFeedback =
                slot.Physical is not { } current ||
                !ReferenceEquals(current.FeedbackSink, feedbackSink) ||
                current.Features != features;
            slot.Physical = new ControllerEndpointState(state, features, feedbackSink);
            if (ShouldTryConnectOutput(slot))
            {
                RefreshOutput(slot);
            }

            send = _activeClientId.HasValue
                ? CreateCurrentStateSend(slot)
                : null;
            if (_activeClientId.HasValue && replayFeedback)
            {
                slot.ReplayFeedback(_activeClientId.Value);
            }
        }

        SendOutput(send);
    }

    /// <summary>Marks a physical controller endpoint as disconnected.</summary>
    public void RemovePhysicalController(ControllerId controllerId)
    {
        ThrowIfDisposed();
        PendingControllerSend? send;
        List<IControllerOutput> dispose = [];

        lock (_gate)
        {
            if (!_slots.TryGetValue(controllerId, out ControllerSlot? slot))
            {
                return;
            }

            slot.RemovePhysical();
            if (!slot.HasEndpoints)
            {
                slot.DisconnectOutput(dispose);
                _ = _slots.Remove(controllerId);
                send = null;
            }
            else
            {
                RefreshOutput(slot, dispose);
                RetargetFeedback();
                send = _activeClientId.HasValue
                    ? CreateCurrentStateSend(slot)
                    : null;
            }
        }

        SendOutput(send);
        DisposeOutputs(dispose);
    }

    // MARK: Control
    // ========================================================================

    /// <summary>Enables or disables all controller output without disconnecting clients.</summary>
    public void SetControllerOutputEnabled(bool enabled)
    {
        ThrowIfDisposed();
        List<PendingControllerSend> sends = [];
        lock (_gate)
        {
            _controllerOutputEnabled = enabled;
            AddCurrentStateSends(sends);
        }

        SendOutputs(sends);
    }

    /// <summary>Enables or disables physical-controller motion fallback.</summary>
    public void SetPhysicalMotionEnabled(bool enabled)
    {
        ThrowIfDisposed();
        List<PendingControllerSend> sends = [];

        lock (_gate)
        {
            _physicalMotionEnabled = enabled;
            if (_activeClientId.HasValue)
            {
                foreach (ControllerSlot slot in _slots.Values)
                {
                    if (CreateCurrentStateSend(slot) is { } send)
                    {
                        sends.Add(send);
                    }
                }
            }
        }

        SendOutputs(sends);
    }

    /// <summary>Gets controller forwarding status.</summary>
    public ControllerBrokerStatus GetStatus()
    {
        ThrowIfDisposed();
        lock (_gate)
        {
            List<ControllerSlotStatus> slots = [];
            foreach (KeyValuePair<ControllerId, ControllerSlot> slot in _slots)
            {
                slots.Add(new ControllerSlotStatus(
                    slot.Key,
                    slot.Value.Output is not null,
                    slot.Value.OutputKind,
                    slot.Value.HasClient(_activeClientId),
                    slot.Value.Physical.HasValue,
                    slot.Value.ClientEndpoints.Count,
                    slot.Value.Physical?.Features,
                    _activeClientId.HasValue && slot.Value.FindClient(_activeClientId.Value) is { } client
                        ? client.Features
                        : null));
            }

            return new ControllerBrokerStatus(
                _activeClientId,
                _controllerOutputEnabled,
                _physicalMotionEnabled,
                slots);
        }
    }

    /// <summary>Disconnects all controller outputs.</summary>
    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <summary>Disconnects all controller outputs.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        List<IControllerOutput> dispose = [];
        lock (_gate)
        {
            foreach (ControllerSlot slot in _slots.Values)
            {
                slot.DisconnectOutput(dispose);
            }

            _slots.Clear();
            _clients.Clear();
        }

        foreach (IControllerOutput output in dispose)
        {
            await output.DisposeAsync().ConfigureAwait(false);
        }
    }

    // MARK: Privates
    // ========================================================================

    private ControllerSlot GetOrCreateSlot(ControllerId controllerId)
    {
        if (!_slots.TryGetValue(controllerId, out ControllerSlot? slot))
        {
            slot = new ControllerSlot(controllerId, HandleFeedback);
            _slots[controllerId] = slot;
        }
        else
        {
            slot.UpdateControllerId(controllerId);
        }

        return slot;
    }

    private void ClearClientStates(Guid clientId)
    {
        foreach (ControllerSlot slot in _slots.Values)
        {
            foreach (ControllerEndpointId endpointId in slot.ClientEndpoints.Keys.ToArray())
            {
                if (endpointId.ClientId != clientId)
                {
                    continue;
                }

                ControllerEndpointState current = slot.ClientEndpoints[endpointId];
                slot.ClientEndpoints[endpointId] = new ControllerEndpointState(
                    ControllerState.Empty,
                    current.Features,
                    current.FeedbackSink,
                    current.CanOwnOutputWithoutPhysical);
            }
        }
    }

    private bool TryFindSlot(ControllerEndpointId endpointId, out ControllerSlot slot)
    {
        foreach (ControllerSlot candidate in _slots.Values)
        {
            if (candidate.ClientEndpoints.ContainsKey(endpointId))
            {
                slot = candidate;
                return true;
            }
        }

        slot = null!;
        return false;
    }

    private bool ShouldTryConnectOutput(ControllerSlot slot)
    {
        return slot.Output is null &&
            ((slot.Physical.HasValue && slot.ClientEndpoints.Count != 0) || slot.HasClientOnlyOutputOwner) &&
            HasOutputClient();
    }

    private void PruneEmptySlots(List<IControllerOutput> dispose)
    {
        foreach (KeyValuePair<ControllerId, ControllerSlot> slot in _slots.ToArray())
        {
            if (slot.Value.HasEndpoints)
            {
                continue;
            }

            slot.Value.DisconnectOutput(dispose);
            _ = _slots.Remove(slot.Key);
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    // MARK: Static Models
    // ========================================================================

    private sealed record ClientEntry(ControllerOutput ControllerOutput);
}
