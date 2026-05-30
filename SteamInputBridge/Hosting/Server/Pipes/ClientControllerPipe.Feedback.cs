using System.IO.Pipes;
using System.Threading.Tasks;
using SteamInputBridge.Forwarding;
using SteamInputBridge.Forwarding.Controller;

namespace SteamInputBridge.Hosting.Server.Pipes;

internal sealed partial class ClientControllerPipe
{
    private async Task RunFeedbackWriteLoopAsync(NamedPipeServerStream pipe, ControllerPipeWriter writer)
    {
        await foreach (ControllerFeedbackFrame frame in _feedbackWrites.Reader.ReadAllAsync(_stop.Token)
            .ConfigureAwait(false))
        {
            if (!pipe.IsConnected)
            {
                return;
            }

            await writer.WriteFeedbackAsync(frame, _stop.Token).ConfigureAwait(false);
            await pipe.FlushAsync(_stop.Token).ConfigureAwait(false);
        }
    }

    private bool QueueFeedback(ushort controllerIndex, ControllerFeedback feedback)
    {
        return _pipe is not null &&
            _pipe.IsConnected &&
            _feedbackWrites.Writer.TryWrite(new ControllerFeedbackFrame(controllerIndex, feedback));
    }

    private sealed class PipeFeedbackSink(
        ClientControllerPipe pipe,
        ushort controllerIndex) : IControllerFeedbackSink
    {
        public bool TrySendFeedback(ControllerFeedback feedback)
        {
            return pipe.QueueFeedback(controllerIndex, feedback);
        }
    }
}
