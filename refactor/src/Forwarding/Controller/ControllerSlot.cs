using System;
using System.Collections.Generic;
using System.Linq;

namespace VirtualMouse.Forwarding;

internal sealed class ControllerSlot(ControllerId controllerId, Action<ControllerSlot, ControllerFeedback> feedback)
{
    private IDisposable? _feedbackSubscription;

    public ControllerId ControllerId { get; private set; } = controllerId;

    public ControllerEndpointState? Physical { get; set; }

    public Dictionary<ControllerEndpointId, ControllerEndpointState> Steam { get; } = [];

    public IControllerOutput? Output { get; private set; }

    public ControllerOutput OutputKind { get; private set; }

    // MARK: Endpoints
    // ========================================================================

    public bool HasSteam(Guid? clientId)
    {
        return clientId.HasValue && FindSteam(clientId.Value) is not null;
    }

    public void RemoveSteam(Guid clientId)
    {
        foreach (ControllerEndpointId endpointId in Steam.Keys.Where(id => id.ClientId == clientId).ToArray())
        {
            _ = Steam.Remove(endpointId);
        }
    }

    public bool TryGetMergedState(
        Guid clientId,
        ControllerFeatures physicalFallbackFeatures,
        out ControllerState state)
    {
        state = default;
        if (FindSteam(clientId) is not { } steam)
        {
            return false;
        }

        ControllerEndpointState? physical = Physical;
        state = new ControllerState(
            Select(steam, physical, physicalFallbackFeatures, ControllerFeatures.StandardControls).Standard,
            Select(steam, physical, physicalFallbackFeatures, ControllerFeatures.Motion).Motion,
            Select(steam, physical, physicalFallbackFeatures, ControllerFeatures.Touchpad).Touchpad);
        return true;
    }

    public bool TrySendFeedback(Guid clientId, ControllerFeedback feedback)
    {
        return
            (FindSteam(clientId) is { } steam &&
            steam.TrySendFeedback(feedback)) ||
            (Physical?.TrySendFeedback(feedback) ?? false);
    }

    public ControllerEndpointState? FindSteam(Guid clientId)
    {
        foreach (KeyValuePair<ControllerEndpointId, ControllerEndpointState> endpoint in Steam)
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
        if (Output is not null && OutputKind == outputKind)
        {
            return;
        }

        DisconnectOutput();
        Output = factory.Connect(ControllerId, outputKind);
        OutputKind = outputKind;
        _feedbackSubscription = Output.ListenFeedback(update => feedback(this, update));
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
        IControllerOutput? output = Output;
        if (output is null)
        {
            return;
        }

        _feedbackSubscription?.Dispose();
        _feedbackSubscription = null;
        Output = null;
        OutputKind = ControllerOutput.None;

        if (dispose is not null)
        {
            dispose.Add(output);
        }
        else
        {
            output.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    // MARK: Privates
    // ========================================================================

    private static ControllerState Select(
        ControllerEndpointState steam,
        ControllerEndpointState? physical,
        ControllerFeatures physicalFallbackFeatures,
        ControllerFeatures feature)
    {
        return steam.Supports(feature)
            ? steam.State
            : physical is { } fallback &&
            (physicalFallbackFeatures & feature) != 0 &&
            fallback.Supports(feature)
            ? fallback.State
            : ControllerState.Empty;
    }
}

internal readonly record struct ControllerEndpointState(
    ControllerState State,
    ControllerFeatures Features,
    IControllerFeedbackSink? FeedbackSink)
{
    public bool Supports(ControllerFeatures feature)
    {
        return (Features & feature) != 0;
    }

    public bool TrySendFeedback(ControllerFeedback feedback)
    {
        return FeedbackSink is not null && FeedbackSink.TrySendFeedback(feedback);
    }
}
