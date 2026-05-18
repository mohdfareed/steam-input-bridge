using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VirtualMouse.Forwarding;

namespace VirtualMouse.Hosting;

internal sealed class ControllerPipeSessions(ControllerBroker broker, ILogger logger) : IAsyncDisposable
{
    private readonly Dictionary<Guid, ClientControllerPipe> _pipes = [];

    public string Start(Guid clientId)
    {
        if (_pipes.TryGetValue(clientId, out ClientControllerPipe? existing))
        {
            return existing.PipeName;
        }

        string pipeName = $"VirtualMouse.Refactor.Controller.{clientId:N}";
        ClientControllerPipe pipe = new(clientId, pipeName, broker, logger);
        _pipes[clientId] = pipe;
        pipe.Start();
        return pipeName;
    }

    public void RegisterControllers(
        Guid clientId,
        IReadOnlyList<ClientControllerInfo> controllers)
    {
        Get(clientId).RegisterControllers(controllers);
    }

    public async Task RemoveAsync(Guid clientId)
    {
        if (_pipes.Remove(clientId, out ClientControllerPipe? pipe))
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
        }
    }

    public IReadOnlyList<ControllerPipeStatus> GetStatus()
    {
        List<ControllerPipeStatus> status = [];
        foreach (KeyValuePair<Guid, ClientControllerPipe> pipe in _pipes)
        {
            status.Add(pipe.Value.GetStatus(pipe.Key));
        }

        return status;
    }

    public async ValueTask DisposeAsync()
    {
        ClientControllerPipe[] pipes = [.. _pipes.Values];
        _pipes.Clear();
        foreach (ClientControllerPipe pipe in pipes)
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
        }
    }

    private ClientControllerPipe Get(Guid clientId)
    {
        return _pipes.TryGetValue(clientId, out ClientControllerPipe? pipe)
            ? pipe
            : throw new InvalidOperationException($"Controller pipe for client {clientId} is not active.");
    }
}

internal sealed class ClientControllerPipe(
    Guid clientId,
    string pipeName,
    ControllerBroker broker,
    ILogger logger) : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Dictionary<ushort, ClientControllerInfo> _controllers = [];
    private readonly Lock _writeGate = new();
    private Task? _task;
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
                    controller.Features));
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

        _stop.Dispose();
    }

    private async Task RunAsync()
    {
        using NamedPipeServerStream pipe = new(
            PipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        _pipe = pipe;
        await pipe.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
        ControllerPipeReader reader = new(pipe);
        _writer = new ControllerPipeWriter(pipe);

        while (!_stop.IsCancellationRequested && pipe.IsConnected)
        {
            ControllerPipeMessage message = await reader.ReadAsync(_stop.Token).ConfigureAwait(false);
            if (message.Type == ControllerPipeFrameType.Input &&
                TryGetController(message.Input.ControllerIndex, out ClientControllerInfo? controller) &&
                controller is not null)
            {
                broker.UpdateClientController(
                    clientId,
                    new ControllerId(controller.PhysicalControllerId),
                    message.Input.State,
                    controller.Features,
                    new PipeFeedbackSink(this, message.Input.ControllerIndex));
            }
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

        logger.LogInformation(
            "Controller pipe for client {ClientId} closed: {Message}",
            clientId,
            exception.Message);
        return false;
    }

    private bool TrySendFeedback(ushort controllerIndex, ControllerFeedback feedback)
    {
        ControllerPipeWriter? writer = _writer;
        NamedPipeServerStream? pipe = _pipe;
        if (writer is null || pipe is null || !pipe.IsConnected)
        {
            return false;
        }

        try
        {
            lock (_writeGate)
            {
                if (!pipe.IsConnected)
                {
                    return false;
                }

                writer.WriteFeedbackAsync(new ControllerFeedbackFrame(controllerIndex, feedback))
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
                pipe.Flush();
            }

            return true;
        }
        catch (Exception exception) when (
            exception is IOException or ObjectDisposedException or InvalidOperationException)
        {
            return false;
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

    private bool QueueFeedback(ushort controllerIndex, ControllerFeedback feedback)
    {
        if (_writer is null || _pipe is null || !_pipe.IsConnected)
        {
            return false;
        }

        _ = Task.Run(() => TrySendFeedback(controllerIndex, feedback), CancellationToken.None);
        return true;
    }
}
