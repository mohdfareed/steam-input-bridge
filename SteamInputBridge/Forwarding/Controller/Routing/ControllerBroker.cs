using System;
using System.Collections.Generic;
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

            ControllerSlot slot = GetOrCreateSlot(physicalControllerId);
            slot.ClientEndpoints[new ControllerEndpointId(clientId, controllerIndex)] =
                new ControllerEndpointState(state, features, feedbackSink);

            RefreshOutput(slot);
            send = _activeClientId == clientId
                ? CreateCurrentStateSend(slot)
                : null;
            if (_activeClientId == clientId)
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
            slot.Physical = new ControllerEndpointState(state, features, feedbackSink);
            send = _activeClientId.HasValue
                ? CreateCurrentStateSend(slot)
                : null;
            if (_activeClientId.HasValue)
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
            send = _activeClientId.HasValue
                ? CreateCurrentStateSend(slot)
                : null;
            if (!slot.HasEndpoints)
            {
                slot.DisconnectOutput(dispose);
                _ = _slots.Remove(controllerId);
            }
            else
            {
                RefreshOutput(slot, dispose);
                RetargetFeedback();
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
        List<IControllerOutput> dispose = [];
        List<PendingControllerSend> sends = [];
        lock (_gate)
        {
            _controllerOutputEnabled = enabled;
            RefreshOutputs(dispose);
            AddCurrentStateSends(sends);
        }

        SendOutputs(sends);
        DisposeOutputs(dispose);
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

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    // MARK: Static Models
    // ========================================================================

    private sealed record ClientEntry(ControllerOutput ControllerOutput);
}
