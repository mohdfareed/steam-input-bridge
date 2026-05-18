using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VirtualMouse.Forwarding;

/// <summary>Routes active-client controller input to game-facing controller outputs.</summary>
public sealed class ControllerBroker(IControllerOutputFactory outputFactory) : IDisposable, IAsyncDisposable
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, ClientEntry> _clients = [];
    private readonly Dictionary<ControllerId, ControllerSlot> _slots = [];
    private Guid? _activeClientId;
    private bool _controllerOutputEnabled = true;
    private bool _physicalMotionEnabled = true;
    private bool _disposed;

    // MARK: API
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

    /// <summary>Removes a client and releases endpoints owned by it.</summary>
    public void RemoveClient(Guid clientId)
    {
        ThrowIfDisposed();
        List<IControllerOutput> dispose = [];
        lock (_gate)
        {
            _ = _clients.Remove(clientId);
            if (_activeClientId == clientId)
            {
                _activeClientId = null;
            }

            foreach (ControllerSlot slot in _slots.Values)
            {
                slot.RemoveSteam(clientId);
            }

            RefreshOutputs(dispose);
        }

        DisposeOutputs(dispose);
    }

    /// <summary>Sets the active client whose controller streams may drive outputs.</summary>
    public void SetActiveClient(Guid? clientId)
    {
        ThrowIfDisposed();
        List<IControllerOutput> dispose = [];
        lock (_gate)
        {
            _activeClientId = clientId.HasValue && _clients.ContainsKey(clientId.Value)
                ? clientId
                : null;
            RefreshOutputs(dispose);
        }

        DisposeOutputs(dispose);
    }

    /// <summary>Enables or disables all controller output without disconnecting clients.</summary>
    public void SetControllerOutputEnabled(bool enabled)
    {
        ThrowIfDisposed();
        List<IControllerOutput> dispose = [];
        lock (_gate)
        {
            _controllerOutputEnabled = enabled;
            RefreshOutputs(dispose);
        }

        DisposeOutputs(dispose);
    }

    /// <summary>Enables or disables physical-controller motion fallback.</summary>
    public void SetPhysicalMotionEnabled(bool enabled)
    {
        ThrowIfDisposed();
        lock (_gate)
        {
            _physicalMotionEnabled = enabled;
            foreach (ControllerSlot slot in _slots.Values)
            {
                SendMergedIfActive(slot);
            }
        }
    }

    /// <summary>Updates the latest physical controller state for one slot.</summary>
    public void UpdatePhysicalController(
        ControllerId controllerId,
        ControllerState state,
        ControllerFeatures features,
        IControllerFeedbackSink? feedbackSink = null)
    {
        ThrowIfDisposed();
        lock (_gate)
        {
            ControllerSlot slot = GetOrCreateSlot(controllerId);
            slot.Physical = new ControllerEndpointState(state, features, feedbackSink);
            SendMergedIfActive(slot);
        }
    }

    /// <summary>Updates a Steam-visible controller stream from one client.</summary>
    public void UpdateClientController(
        Guid clientId,
        ControllerId physicalControllerId,
        ControllerState state,
        ControllerFeatures features,
        IControllerFeedbackSink? feedbackSink = null)
    {
        ThrowIfDisposed();
        lock (_gate)
        {
            if (!_clients.ContainsKey(clientId))
            {
                return;
            }

            ControllerSlot slot = GetOrCreateSlot(physicalControllerId);
            slot.Steam[clientId] = new ControllerEndpointState(state, features, feedbackSink);
            RefreshOutput(slot);
            SendMergedIfActive(slot);
        }
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
                    slot.Value.HasSteam(_activeClientId),
                    slot.Value.Physical.HasValue,
                    slot.Value.Steam.Count,
                    slot.Value.Physical?.Features,
                    _activeClientId.HasValue &&
                        slot.Value.Steam.TryGetValue(_activeClientId.Value, out ControllerEndpointState steam)
                        ? steam.Features
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

    // MARK: Helpers
    // ========================================================================

    private ControllerSlot GetOrCreateSlot(ControllerId controllerId)
    {
        if (!_slots.TryGetValue(controllerId, out ControllerSlot? slot))
        {
            slot = new ControllerSlot(controllerId, HandleFeedback);
            _slots[controllerId] = slot;
        }

        return slot;
    }

    private void RefreshOutputs(List<IControllerOutput>? dispose = null)
    {
        foreach (ControllerSlot slot in _slots.Values)
        {
            RefreshOutput(slot, dispose);
        }
    }

    private void RefreshOutput(ControllerSlot slot, List<IControllerOutput>? dispose = null)
    {
        ControllerOutput outputKind = GetActiveOutputKind();
        bool shouldConnect =
            _controllerOutputEnabled &&
            outputKind != ControllerOutput.None &&
            slot.HasSteam(_activeClientId);

        if (!shouldConnect)
        {
            slot.DisconnectOutput(dispose);
            return;
        }

        slot.ConnectOutput(outputFactory, outputKind);
    }

    private ControllerOutput GetActiveOutputKind()
    {
        return _activeClientId.HasValue &&
            _clients.TryGetValue(_activeClientId.Value, out ClientEntry? client)
            ? client.ControllerOutput
            : ControllerOutput.None;
    }

    private void SendMergedIfActive(ControllerSlot slot)
    {
        if (slot.Output is null ||
            !_activeClientId.HasValue ||
            !slot.TryGetMergedState(
                _activeClientId.Value,
                GetPhysicalFallbackFeatures(),
                out ControllerState state))
        {
            return;
        }

        slot.Output.Send(in state);
    }

    private void HandleFeedback(ControllerSlot slot, ControllerFeedback feedback)
    {
        lock (_gate)
        {
            if (!_activeClientId.HasValue)
            {
                return;
            }

            _ = slot.TrySendFeedback(_activeClientId.Value, feedback);
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private ControllerFeatures GetPhysicalFallbackFeatures()
    {
        ControllerFeatures features =
            ControllerFeatures.StandardControls |
            ControllerFeatures.Touchpad |
            ControllerFeatures.Light |
            ControllerFeatures.Rumble |
            ControllerFeatures.AdaptiveTriggers;

        return _physicalMotionEnabled
            ? features | ControllerFeatures.Motion
            : features;
    }

    private static void DisposeOutputs(List<IControllerOutput> outputs)
    {
        foreach (IControllerOutput output in outputs)
        {
            output.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private sealed record ClientEntry(ControllerOutput ControllerOutput);
}

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
    bool HasActiveSteamEndpoint,
    bool HasPhysicalEndpoint,
    int SteamEndpointCount,
    ControllerFeatures? PhysicalFeatures,
    ControllerFeatures? ActiveSteamFeatures);
