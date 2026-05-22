using System;
using System.Threading;
using System.Threading.Tasks;
using SteamInputBridge.Forwarding.Controller;
using SteamInputBridge.Forwarding.Mouse;

namespace SteamInputBridge.Hosting.Server;

internal sealed class NoopControllerOutputFactory : IControllerOutputFactory
{
    public IControllerOutput Connect(ControllerId controllerId, ControllerOutput output)
    {
        _ = controllerId;
        _ = output;
        return new NoopControllerOutput();
    }

    private sealed class NoopControllerOutput : IControllerOutput
    {
        public void Send(in ControllerState state)
        {
            _ = state;
        }

        public IDisposable ListenFeedback(Action<ControllerFeedback> handler)
        {
            _ = handler;
            return new Subscription();
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}

internal sealed class NoopMouseOutputFactory : IMouseOutputFactory
{
    public IMouseOutput Connect(MouseOutput output)
    {
        _ = output;
        return new NoopMouseOutput();
    }

    private sealed class NoopMouseOutput : IMouseOutput
    {
        public bool IsConnected => true;

        public ValueTask SendAsync(MouseReport report, CancellationToken cancellationToken = default)
        {
            _ = report;
            _ = cancellationToken;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}

internal sealed class Subscription : IDisposable
{
    public void Dispose()
    {
    }
}
