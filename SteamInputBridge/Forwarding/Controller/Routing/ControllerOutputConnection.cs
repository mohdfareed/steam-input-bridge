using System;
using System.Collections.Generic;

namespace SteamInputBridge.Forwarding.Controller.Routing;

internal sealed class ControllerOutputConnection
{
    private IDisposable? _feedbackSubscription;

    public IControllerOutput? Output { get; private set; }

    public ControllerOutput OutputKind { get; private set; }

    public void Connect(
        IControllerOutputFactory factory,
        ControllerId controllerId,
        ControllerOutput outputKind,
        Action<ControllerFeedback> feedback)
    {
        if (Output is not null && OutputKind == outputKind)
        {
            return;
        }

        Disconnect();
        Output = factory.Connect(controllerId, outputKind);
        OutputKind = outputKind;
        _feedbackSubscription = Output.ListenFeedback(feedback);
    }

    public void Disconnect(List<IControllerOutput>? dispose = null)
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
}
