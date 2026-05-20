using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Forwarding;
using SteamInputBridge.Forwarding.Controller;

namespace SteamInputBridge.Hosting.Server.Pipes;

internal sealed class ClientControllerPipe(
    Guid clientId,
    string pipeName,
    ControllerBroker broker,
    ILogger logger) : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Dictionary<ushort, ClientControllerInfo> _controllers = [];
    private readonly Channel<ControllerFeedbackFrame> _feedbackWrites =
        Channel.CreateBounded<ControllerFeedbackFrame>(
            new BoundedChannelOptions(capacity: 32)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            });
    private Task? _task;
    private Task? _feedbackTask;
    private NamedPipeServerStream? _pipe;
    private ControllerPipeWriter? _writer;

    public string PipeName { get; } = pipeName;

    public void Start()
    {
        _task = Task.Run(RunAsync, CancellationToken.None);
    }

    public void RegisterControllers(IReadOnlyList<ClientControllerInfo> controllers)
    {
        ArgumentNullException.ThrowIfNull(controllers);
        broker.RemoveClientControllers(clientId);

        lock (_controllers)
        {
            _controllers.Clear();
            foreach (ClientControllerInfo controller in controllers)
            {
                _controllers[controller.ControllerIndex] = controller;
            }
        }
    }

    public ControllerPipeStatus GetStatus(Guid clientId)
    {
        List<ClientControllerStatus> controllers = [];
        lock (_controllers)
        {
            foreach (ClientControllerInfo controller in _controllers.Values)
            {
                controllers.Add(new ClientControllerStatus(
                    controller.ControllerIndex,
                    controller.PhysicalControllerId,
                    controller.Label,
                    controller.Features,
                    controller.PhysicalDeviceId));
            }
        }

        return new ControllerPipeStatus(
            clientId,
            PipeName,
            _pipe?.IsConnected == true,
            controllers);
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        _pipe?.Dispose();
        if (_task is not null)
        {
            try
            {
                await _task.ConfigureAwait(false);
            }
            catch (Exception exception) when (IsExpectedStop(exception))
            {
            }
        }

        await IgnoreExpectedStopAsync(_feedbackTask).ConfigureAwait(false);
        _stop.Dispose();
    }

    private async Task RunAsync()
    {
        NamedPipeServerStream? pipe = null;
        try
        {
            pipe = new NamedPipeServerStream(
                PipeName,
                PipeDirection.InOut,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
            _pipe = pipe;
            await pipe.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
            ControllerPipeReader reader = new(pipe);
            _writer = new ControllerPipeWriter(pipe);
            _feedbackTask = Task.Run(() => RunFeedbackWriteLoopAsync(pipe), CancellationToken.None);

            try
            {
                while (!_stop.IsCancellationRequested && pipe.IsConnected)
                {
                    ControllerPipeMessage message = await reader.ReadAsync(_stop.Token).ConfigureAwait(false);
                    if (message.Type == ControllerPipeFrameType.Input &&
                        TryGetController(message.Input.ControllerIndex, out ClientControllerInfo? controller) &&
                        controller is not null)
                    {
                        broker.UpdateClientController(
                            clientId,
                            message.Input.ControllerIndex,
                            new ControllerId(controller.PhysicalControllerId, controller.Label),
                            message.Input.State,
                            controller.Features,
                            new PipeFeedbackSink(this, message.Input.ControllerIndex));
                    }
                }
            }
            finally
            {
                await _stop.CancelAsync().ConfigureAwait(false);
                await IgnoreExpectedStopAsync(_feedbackTask).ConfigureAwait(false);
            }
        }
        finally
        {
            if (ReferenceEquals(_pipe, pipe))
            {
                _pipe = null;
            }

            if (pipe is not null)
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task RunFeedbackWriteLoopAsync(NamedPipeServerStream pipe)
    {
        ControllerPipeWriter writer = _writer ??
            throw new InvalidOperationException("Controller pipe writer is not connected.");

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

    private bool TryGetController(ushort controllerIndex, out ClientControllerInfo? controller)
    {
        lock (_controllers)
        {
            return _controllers.TryGetValue(controllerIndex, out controller);
        }
    }

    private bool IsExpectedStop(Exception exception)
    {
        if (exception is OperationCanceledException or IOException or ObjectDisposedException)
        {
            return true;
        }

        HostingLog.ControllerPipeClosed(logger, clientId, exception.Message);
        return false;
    }

    private bool QueueFeedback(ushort controllerIndex, ControllerFeedback feedback)
    {
        return _writer is not null &&
            _pipe is not null &&
            _pipe.IsConnected &&
            _feedbackWrites.Writer.TryWrite(new ControllerFeedbackFrame(controllerIndex, feedback));
    }

    private static async Task IgnoreExpectedStopAsync(Task? task)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is OperationCanceledException or IOException or ObjectDisposedException)
        {
        }
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
