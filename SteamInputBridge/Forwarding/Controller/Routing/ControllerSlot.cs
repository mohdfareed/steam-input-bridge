using System;
using System.Collections.Generic;
using System.Linq;

namespace SteamInputBridge.Forwarding.Controller.Routing;

internal sealed partial class ControllerSlot(ControllerId controllerId, Action<ControllerSlot, ControllerFeedback> feedback)
{
    private readonly ControllerOutputConnection _output = new();
    private ControllerFeedback? _heldFeedback;
    private FeedbackTarget? _feedbackTarget;

    public ControllerId ControllerId { get; private set; } = controllerId;

    public ControllerEndpointState? Physical { get; set; }

    public Dictionary<ControllerEndpointId, ControllerEndpointState> ClientEndpoints { get; } = [];

    public IControllerOutput? Output => _output.Output;

    public ControllerOutput OutputKind => _output.OutputKind;

    public bool HasEndpoints => Physical.HasValue || ClientEndpoints.Count != 0;

    public bool HasClientOnlyOutputOwner
    {
        get
        {
            foreach (ControllerEndpointState endpoint in ClientEndpoints.Values)
            {
                if (endpoint.CanOwnOutputWithoutPhysical)
                {
                    return true;
                }
            }

            return false;
        }
    }

    // MARK: Endpoints
    // ========================================================================

    public bool HasClient(Guid? clientId)
    {
        return clientId.HasValue && FindClient(clientId.Value) is not null;
    }

    public void RemoveClient(Guid clientId)
    {
        foreach (ControllerEndpointId endpointId in ClientEndpoints.Keys.Where(id => id.ClientId == clientId).ToArray())
        {
            RemoveClientController(endpointId);
        }
    }

    public void RemoveClientController(ControllerEndpointId endpointId)
    {
        if (!ClientEndpoints.ContainsKey(endpointId))
        {
            return;
        }

        StopFeedbackTarget(new FeedbackTarget(endpointId));
        _ = ClientEndpoints.Remove(endpointId);
    }

    public void RemovePhysical()
    {
        StopFeedbackTarget(FeedbackTarget.Physical);
        Physical = null;
    }

    public bool TryGetMergedState(
        Guid clientId,
        ControllerFeatures clientFeatures,
        ControllerFeatures physicalFallbackFeatures,
        out ControllerState state)
    {
        state = default;
        if (FindClient(clientId) is not { } client)
        {
            // A physical slot exists before its matching Steam/client endpoint
            // is resolved. Do not let it drive output by itself; otherwise a
            // profile can bypass Steam Input mapping and look like held/raw
            // physical input. Physical state is only a companion fallback once
            // the active client stream is attached to this exact slot.
            return false;
        }

        ControllerEndpointState? physical = Physical;
        state = new ControllerState(
            MergeStandardControls(client, physical, clientFeatures, physicalFallbackFeatures),
            Select(client, physical, clientFeatures, physicalFallbackFeatures, ControllerFeatures.Motion)
                .Motion,
            MergeTouchpad(client, physical, clientFeatures, physicalFallbackFeatures));
        return true;
    }

    public ControllerEndpointState? FindClient(Guid clientId)
    {
        foreach (KeyValuePair<ControllerEndpointId, ControllerEndpointState> endpoint in ClientEndpoints)
        {
            if (endpoint.Key.ClientId == clientId)
            {
                return endpoint.Value;
            }
        }

        return null;
    }

    // MARK: Output
    // ========================================================================

    public void ConnectOutput(IControllerOutputFactory factory, ControllerOutput outputKind)
    {
        _output.Connect(factory, ControllerId, outputKind, update => feedback(this, update));
    }

    public void UpdateControllerId(ControllerId controllerId)
    {
        if (string.IsNullOrWhiteSpace(ControllerId.DisplayName) &&
            !string.IsNullOrWhiteSpace(controllerId.DisplayName))
        {
            ControllerId = controllerId;
        }
    }

    public void DisconnectOutput(List<IControllerOutput>? dispose = null)
    {
        StopHeldFeedback();
        _output.Disconnect(dispose);
    }

    // MARK: Privates
    // ========================================================================

    private static ControllerStandardState? MergeStandardControls(
        ControllerEndpointState client,
        ControllerEndpointState? physical,
        ControllerFeatures clientFeatures,
        ControllerFeatures physicalFallbackFeatures)
    {
        ControllerStandardState? clientState =
            CanUse(client, clientFeatures, ControllerFeatures.StandardControls)
                ? client.State.Standard
                : null;
        ControllerStandardState? physicalState =
            physical is { } fallback &&
            CanUse(fallback, physicalFallbackFeatures, ControllerFeatures.StandardControls)
                ? fallback.State.Standard
                : null;

        if (clientState is not { } standard)
        {
            return physicalState;
        }

        if (physicalState is not { } physicalStandard)
        {
            return standard;
        }

        // Steam Input can expose normal buttons/sticks while omitting analog
        // trigger axes for a controller. Keep Steam's remapped standard state,
        // but fill missing trigger axes from the physical companion.
        return standard with
        {
            LeftTrigger = standard.LeftTrigger == 0
                ? physicalStandard.LeftTrigger
                : standard.LeftTrigger,
            RightTrigger = standard.RightTrigger == 0
                ? physicalStandard.RightTrigger
                : standard.RightTrigger,
        };
    }

    private static ControllerTouchpadState? MergeTouchpad(
        ControllerEndpointState client,
        ControllerEndpointState? physical,
        ControllerFeatures clientFeatures,
        ControllerFeatures physicalFallbackFeatures)
    {
        ControllerTouchpadState? clientTouchpad =
            CanUse(client, clientFeatures, ControllerFeatures.Touchpad)
                ? client.State.Touchpad
                : null;
        ControllerTouchpadState? physicalTouchpad =
            physical is { } fallback &&
            CanUse(fallback, physicalFallbackFeatures, ControllerFeatures.Touchpad)
                ? fallback.State.Touchpad
                : null;

        if (clientTouchpad is not { } touchpad)
        {
            return physicalTouchpad;
        }

        if (physicalTouchpad is not { } physicalFallback)
        {
            return touchpad;
        }

        // SDL, Steam Input, and physical companions may expose different
        // touch-contact counts. Keep the client contact positions that exist,
        // but fill missing contacts from the matched physical slot.
        return new ControllerTouchpadState(
            touchpad.Touch1.IsTouched ? touchpad.Touch1 : physicalFallback.Touch1,
            touchpad.Touch2.IsTouched ? touchpad.Touch2 : physicalFallback.Touch2);
    }

    private static ControllerState Select(
        ControllerEndpointState client,
        ControllerEndpointState? physical,
        ControllerFeatures clientFeatures,
        ControllerFeatures physicalFallbackFeatures,
        ControllerFeatures feature)
    {
        return CanUse(client, clientFeatures, feature)
            ? client.State
            : physical is { } fallback &&
            CanUse(fallback, physicalFallbackFeatures, feature)
            ? fallback.State
            : ControllerState.Empty;
    }

    private static bool CanUse(
        ControllerEndpointState endpoint,
        ControllerFeatures enabledFeatures,
        ControllerFeatures feature)
    {
        return (enabledFeatures & feature) != 0 && endpoint.Supports(feature);
    }
}
