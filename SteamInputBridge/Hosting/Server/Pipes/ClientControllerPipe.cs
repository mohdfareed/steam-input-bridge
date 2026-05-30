using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Forwarding;
using SteamInputBridge.Hosting.Server.Orchestration.Lifetime;

namespace SteamInputBridge.Hosting.Server.Pipes;

internal sealed partial class ClientControllerPipe(
    Guid clientId,
    string pipeName,
    Forwarding.Controller.Routing.ControllerBroker broker,
    ILogger logger,
    IPhysicalControllerResolver? physicalControllers = null) : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly IPhysicalControllerResolver? _physicalControllers = physicalControllers;
    private readonly Dictionary<ushort, ClientControllerInfo> _requestedControllers = [];
    private readonly Dictionary<ushort, ClientControllerInfo> _controllers = [];
    private readonly Dictionary<ushort, long> _inputFrameCounts = [];
    private readonly Dictionary<ushort, PipeFeedbackSink> _feedbackSinks = [];
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

    public string PipeName { get; } = pipeName;

    public void Start()
    {
        _task = Task.Run(RunAsync, CancellationToken.None);
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

        _physicalControllers?.RemoveClient(clientId);
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
            ControllerPipeWriter writer = new(pipe);
            _feedbackTask = Task.Run(() => RunFeedbackWriteLoopAsync(pipe, writer), CancellationToken.None);

            try
            {
                while (!_stop.IsCancellationRequested && pipe.IsConnected)
                {
                    ControllerPipeMessage message = await reader.ReadAsync(_stop.Token).ConfigureAwait(false);
                    if (message.Type != ControllerPipeFrameType.Input)
                    {
                        continue;
                    }

                    _physicalControllers?.ObserveClientControllerInput(
                        clientId,
                        message.Input.ControllerIndex,
                        message.Input.State);
                    if (TryBeginInputFrame(
                        message.Input.ControllerIndex,
                        out ClientControllerInfo controller,
                        out PipeFeedbackSink feedbackSink))
                    {
                        broker.UpdateClientController(
                            clientId,
                            message.Input.ControllerIndex,
                            message.Input.State,
                            controller.Features,
                            feedbackSink);
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

    private bool TryBeginInputFrame(
        ushort controllerIndex,
        out ClientControllerInfo controller,
        out PipeFeedbackSink feedbackSink)
    {
        lock (_controllers)
        {
            if (!_controllers.TryGetValue(controllerIndex, out ClientControllerInfo? foundController))
            {
                controller = null!;
                feedbackSink = null!;
                return false;
            }

            _ = _inputFrameCounts.TryGetValue(controllerIndex, out long count);
            _inputFrameCounts[controllerIndex] = count + 1;

            if (!_feedbackSinks.TryGetValue(controllerIndex, out PipeFeedbackSink? sink))
            {
                sink = new PipeFeedbackSink(this, controllerIndex);
                _feedbackSinks[controllerIndex] = sink;
            }

            controller = foundController;
            feedbackSink = sink;
            return true;
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
}
