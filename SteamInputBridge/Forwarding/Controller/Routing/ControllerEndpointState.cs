namespace SteamInputBridge.Forwarding.Controller.Routing;

internal readonly record struct ControllerEndpointState(
    ControllerState State,
    ControllerFeatures Features,
    IControllerFeedbackSink? FeedbackSink)
{
    public bool Supports(ControllerFeatures feature)
    {
        return (Features & feature) == feature;
    }

    public bool CanAccept(ControllerFeedback feedback)
    {
        ControllerFeatures required = feedback.RequiredFeatures;
        return FeedbackSink is not null &&
            required != ControllerFeatures.None &&
            (Features & required) != ControllerFeatures.None;
    }

    public bool CanAcceptAll(ControllerFeedback feedback)
    {
        ControllerFeatures required = feedback.RequiredFeatures;
        return FeedbackSink is not null &&
            required != ControllerFeatures.None &&
            Supports(required);
    }

    public bool TrySendFeedback(ControllerFeedback feedback)
    {
        ControllerFeedback supported = feedback.ForFeatures(Features);
        return !supported.IsEmpty && FeedbackSink is not null && FeedbackSink.TrySendFeedback(supported);
    }
}

internal readonly record struct FeedbackTarget(ControllerEndpointId? EndpointId)
{
    public static FeedbackTarget Physical { get; } = new(null);
}
