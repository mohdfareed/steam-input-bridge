using System;
using System.Collections.Generic;

namespace SteamInputBridge.Forwarding.Controller.Routing;

internal sealed partial class ControllerSlot
{
    public void ApplyFeedback(Guid clientId, ControllerFeedback feedback)
    {
        FeedbackTarget? previous = _feedbackTarget;
        FeedbackTarget? target = null;

        foreach (FeedbackTarget candidate in FindFeedbackTargets(clientId, feedback))
        {
            if (SendFeedback(candidate, feedback))
            {
                target = candidate;
                break;
            }
        }

        if (previous is { } previousTarget && previousTarget != target)
        {
            StopFeedbackTarget(previousTarget);
        }

        _heldFeedback = feedback;
        _feedbackTarget = target;
    }

    public void RetargetFeedback(Guid? clientId)
    {
        if (_heldFeedback is not { } feedback)
        {
            return;
        }

        if (!clientId.HasValue)
        {
            StopHeldFeedback();
            return;
        }

        ApplyFeedback(clientId.Value, feedback);
    }

    public void ReplayFeedback(Guid clientId)
    {
        if (_heldFeedback is { } feedback)
        {
            ApplyFeedback(clientId, feedback);
        }
    }

    public void StopHeldFeedback()
    {
        if (_feedbackTarget is { } target)
        {
            StopFeedbackTarget(target);
        }

        _heldFeedback = null;
        _feedbackTarget = null;
    }

    private IEnumerable<FeedbackTarget> FindFeedbackTargets(Guid clientId, ControllerFeedback feedback)
    {
        if (feedback.IsEmpty)
        {
            yield break;
        }

        foreach (KeyValuePair<ControllerEndpointId, ControllerEndpointState> endpoint in ClientEndpoints)
        {
            if (endpoint.Key.ClientId == clientId && endpoint.Value.CanAccept(feedback))
            {
                yield return new FeedbackTarget(endpoint.Key);
            }
        }

        if (Physical is { } physical && physical.CanAccept(feedback))
        {
            yield return FeedbackTarget.Physical;
        }
    }

    private bool SendFeedback(FeedbackTarget target, ControllerFeedback feedback)
    {
        return target.EndpointId is { } endpointId
            ? ClientEndpoints.TryGetValue(endpointId, out ControllerEndpointState client) &&
            client.TrySendFeedback(feedback)
            : Physical?.TrySendFeedback(feedback) ?? false;
    }

    private void StopFeedbackTarget(FeedbackTarget target)
    {
        if (_heldFeedback is null)
        {
            return;
        }

        _ = SendFeedback(target, ControllerFeedback.StopRumble);
    }
}
