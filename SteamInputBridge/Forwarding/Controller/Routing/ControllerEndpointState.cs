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
        return FeedbackSink is not null && required != ControllerFeatures.None && Supports(required);
    }

    public bool TrySendFeedback(ControllerFeedback feedback)
    {
        return CanAccept(feedback) && FeedbackSink!.TrySendFeedback(feedback);
    }
}

internal readonly record struct FeedbackTarget(ControllerEndpointId? EndpointId)
{
    public static FeedbackTarget Physical { get; } = new(null);
}
