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
            return false;
        }

        ControllerEndpointState? physical = Physical;
        state = new ControllerState(
            Select(client, physical, clientFeatures, physicalFallbackFeatures, ControllerFeatures.StandardControls)
                .Standard,
            Select(client, physical, clientFeatures, physicalFallbackFeatures, ControllerFeatures.Motion)
                .Motion,
            Select(client, physical, clientFeatures, physicalFallbackFeatures, ControllerFeatures.Touchpad)
                .Touchpad);
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

    private static ControllerState Select(
        ControllerEndpointState client,
        ControllerEndpointState? physical,
        ControllerFeatures clientFeatures,
        ControllerFeatures physicalFallbackFeatures,
        ControllerFeatures feature)
    {
        return (clientFeatures & feature) != 0 && client.Supports(feature)
            ? client.State
            : physical is { } fallback &&
            (physicalFallbackFeatures & feature) != 0 &&
            fallback.Supports(feature)
            ? fallback.State
            : ControllerState.Empty;
    }
}
