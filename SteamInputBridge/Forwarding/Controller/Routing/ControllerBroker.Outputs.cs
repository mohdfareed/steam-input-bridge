using System.Collections.Generic;

namespace SteamInputBridge.Forwarding.Controller.Routing;

public sealed partial class ControllerBroker
{
    private void RefreshOutputs(List<IControllerOutput>? dispose = null)
    {
        foreach (ControllerSlot slot in _slots.Values)
        {
            RefreshOutput(slot, dispose);
        }
    }

    private void RefreshOutput(ControllerSlot slot, List<IControllerOutput>? dispose = null)
    {
        ControllerOutput outputKind = GetOutputKind(slot, keepExistingWhenInactive: true);
        // Unknown Steam streams stay candidates until they are matched to a
        // physical slot. Physical-only slots therefore do not create virtual
        // controllers; output appears only after a resolved client endpoint is
        // attached. Real Steam Controllers are the explicit client-only
        // exception when no host-visible physical counterpart exists.
        // Output enable/disable is a report gate, not a device lifecycle
        // gate. Keep virtual devices connected so Windows and Steam do not
        // rebuild controller streams when a shortcut disables forwarding.
        bool shouldConnect =
            ((slot.Physical.HasValue && slot.ClientEndpoints.Count != 0) || slot.HasClientOnlyOutputOwner) &&
            outputKind != ControllerOutput.None;

        if (!shouldConnect)
        {
            slot.DisconnectOutput(dispose);
            return;
        }

        slot.ConnectOutput(outputFactory, outputKind);
    }

    private ControllerOutput GetOutputKind(ControllerSlot slot, bool keepExistingWhenInactive)
    {
        if (_activeClientId.HasValue &&
            _clients.TryGetValue(_activeClientId.Value, out ClientEntry? activeClient) &&
            activeClient.ControllerOutput != ControllerOutput.None)
        {
            return activeClient.ControllerOutput;
        }

        if (keepExistingWhenInactive &&
            slot.OutputKind != ControllerOutput.None &&
            HasOutputClient())
        {
            return slot.OutputKind;
        }

        foreach (ControllerEndpointId endpointId in slot.ClientEndpoints.Keys)
        {
            if (_clients.TryGetValue(endpointId.ClientId, out ClientEntry? client) &&
                client.ControllerOutput != ControllerOutput.None)
            {
                return client.ControllerOutput;
            }
        }

        return ControllerOutput.None;
    }

    private bool HasOutputClient()
    {
        foreach (ClientEntry client in _clients.Values)
        {
            if (client.ControllerOutput != ControllerOutput.None)
            {
                return true;
            }
        }

        return false;
    }

    private void AddCurrentStateSends(List<PendingControllerSend> sends)
    {
        foreach (ControllerSlot slot in _slots.Values)
        {
            if (CreateCurrentStateSend(slot) is { } send)
            {
                sends.Add(send);
            }
        }
    }

    private PendingControllerSend? CreateCurrentStateSend(ControllerSlot slot)
    {
        if (slot.Output is null)
        {
            return null;
        }

        if (!_controllerOutputEnabled ||
            !_activeClientId.HasValue ||
            !slot.TryGetMergedState(
                _activeClientId.Value,
                GetClientFeatures(),
                GetPhysicalFallbackFeatures(),
                out ControllerState state))
        {
            state = ControllerState.Empty;
        }

        return new PendingControllerSend(slot.Output, state);
    }

    private void HandleFeedback(ControllerSlot slot, ControllerFeedback feedback)
    {
        lock (_gate)
        {
            if (!_activeClientId.HasValue)
            {
                return;
            }

            slot.ApplyFeedback(_activeClientId.Value, feedback);
        }
    }

    private ControllerFeatures GetPhysicalFallbackFeatures()
    {
        return GetClientFeatures();
    }

    private ControllerFeatures GetClientFeatures()
    {
        ControllerFeatures features = ControllerFeatures.StandardControls | ControllerFeatures.Touchpad;
        return _physicalMotionEnabled
            ? features | ControllerFeatures.Motion
            : features;
    }

    private void RetargetFeedback()
    {
        foreach (ControllerSlot slot in _slots.Values)
        {
            slot.RetargetFeedback(_activeClientId);
        }
    }

    private static void DisposeOutputs(List<IControllerOutput> outputs)
    {
        foreach (IControllerOutput output in outputs)
        {
            output.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static void SendOutput(PendingControllerSend? send)
    {
        if (send is { } value)
        {
            ControllerState state = value.State;
            value.Output.Send(in state);
        }
    }

    private static void SendOutputs(List<PendingControllerSend> sends)
    {
        foreach (PendingControllerSend send in sends)
        {
            ControllerState state = send.State;
            send.Output.Send(in state);
        }
    }

    private readonly record struct PendingControllerSend(
        IControllerOutput Output,
        ControllerState State);
}
