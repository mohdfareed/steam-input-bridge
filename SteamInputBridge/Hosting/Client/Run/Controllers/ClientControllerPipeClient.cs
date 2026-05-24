using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using SteamInputBridge.Forwarding;
using SteamInputBridge.Forwarding.Controller;
using SteamInputBridge.Inputs.Sdl;

namespace SteamInputBridge.Hosting.Client.Run.Controllers;

internal sealed class ClientControllerPipeClient(
    ClientControllerSourceRegistry sources,
    CancellationTokenSource stop) : IAsyncDisposable
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);

    private readonly Channel<ControllerInputFrame> _inputWrites = Channel.CreateBounded<ControllerInputFrame>(
        new BoundedChannelOptions(capacity: 128)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });
    private NamedPipeClientStream? _pipe;
    private ControllerPipeWriter? _writer;
    private Task? _writeTask;
    private Task? _feedbackTask;

    public async Task ConnectAsync(string pipeName, CancellationToken cancellationToken)
    {
        NamedPipeClientStream pipe = new(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        await pipe.ConnectAsync(cancellationToken).ConfigureAwait(false);

        _pipe = pipe;
        _writer = new ControllerPipeWriter(pipe);
        ControllerPipeReader reader = new(pipe);

        _writeTask = Task.Run(RunInputWriteLoopAsync, CancellationToken.None);
        _feedbackTask = Task.Run(() => RunFeedbackLoopAsync(reader), CancellationToken.None);
    }

    public void SendInput(SdlGamepadSource source, ControllerState state)
    {
        if (_writer is null || stop.IsCancellationRequested)
        {
            return;
        }

        if (!sources.TryFindSourceIndex(source, out ushort controllerIndex))
        {
            return;
        }

        _ = _inputWrites.Writer.TryWrite(new ControllerInputFrame(controllerIndex, state));
    }

    public async ValueTask DisposeAsync()
    {
        _ = _inputWrites.Writer.TryComplete();
        _pipe?.Dispose();
        await IgnoreExpectedStopAsync(_writeTask).ConfigureAwait(false);
        await IgnoreExpectedStopAsync(_feedbackTask).ConfigureAwait(false);
    }

    private async Task RunFeedbackLoopAsync(ControllerPipeReader reader)
    {
        while (!stop.IsCancellationRequested)
        {
            ControllerPipeMessage message = await reader.ReadAsync(stop.Token).ConfigureAwait(false);
            if (message.Type == ControllerPipeFrameType.Feedback &&
                sources.TryGetSource(message.Feedback.ControllerIndex, out SdlGamepadSource? source))
            {
                _ = source.TrySendFeedback(message.Feedback.Feedback);
            }
        }
    }

    private async Task RunInputWriteLoopAsync()
    {
        ControllerPipeWriter writer = _writer ??
            throw new InvalidOperationException("Controller pipe writer is not connected.");

        await foreach (ControllerInputFrame frame in _inputWrites.Reader.ReadAllAsync(stop.Token)
            .ConfigureAwait(false))
        {
            await writer.WriteInputAsync(frame, stop.Token).ConfigureAwait(false);
        }
    }

    private static async Task IgnoreExpectedStopAsync(Task? task)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            await task.WaitAsync(StopTimeout).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is OperationCanceledException or IOException or ObjectDisposedException or TimeoutException)
        {
        }
    }
}
