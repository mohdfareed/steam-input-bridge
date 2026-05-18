using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VirtualMouse.Forwarding;
using VirtualMouse.Inputs.Sdl;

namespace VirtualMouse.Hosting;

internal sealed class ClientControllerStreams(ILogger logger) : IAsyncDisposable
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    private readonly CancellationTokenSource _stop = new();
    private readonly Lock _writeGate = new();
    private readonly Lock _sourcesGate = new();
    private IReadOnlyList<SdlGamepadSource> _sources = [];
    private NamedPipeClientStream? _pipe;
    private ControllerPipeWriter? _writer;
    private Task? _inputTask;
    private Task? _feedbackTask;

    public async Task StartAsync(
        VirtualMouseClient client,
        ClientRunLaunch launch,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(launch);

        NamedPipeClientStream pipe = new(
            ".",
            launch.ControllerPipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        await pipe.ConnectAsync(cancellationToken).ConfigureAwait(false);
        _pipe = pipe;
        _writer = new ControllerPipeWriter(pipe);
        ControllerPipeReader reader = new(pipe);

        _inputTask = Task.Run(() => RunInputLoopAsync(client), CancellationToken.None);
        _feedbackTask = Task.Run(() => RunFeedbackLoopAsync(reader), CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        _pipe?.Dispose();
        await IgnoreExpectedStopAsync(_inputTask).ConfigureAwait(false);
        await IgnoreExpectedStopAsync(_feedbackTask).ConfigureAwait(false);

        await DisposeSourcesAsync().ConfigureAwait(false);

        _stop.Dispose();
    }

    private async Task RunInputLoopAsync(VirtualMouseClient client)
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                IReadOnlyList<SdlGamepadSource> sources = await RefreshSourcesAsync(client, _stop.Token)
                    .ConfigureAwait(false);
                if (sources.Count == 0)
                {
                    await Task.Delay(RetryDelay, _stop.Token).ConfigureAwait(false);
                    continue;
                }

                SdlGamepadEventLoop.Run(sources, SendInput, _stop.Token);
            }
            catch (Exception exception) when (
                exception is SdlGamepadDisconnectedException or
                    InvalidOperationException or
                    IOException or
                    ObjectDisposedException)
            {
                if (_stop.IsCancellationRequested)
                {
                    return;
                }

                logger.LogInformation("SDL controller streaming restarting: {Message}", exception.Message);
                await Task.Delay(RetryDelay, _stop.Token).ConfigureAwait(false);
            }
        }
    }

    private async Task RunFeedbackLoopAsync(ControllerPipeReader reader)
    {
        while (!_stop.IsCancellationRequested)
        {
            ControllerPipeMessage message = await reader.ReadAsync(_stop.Token).ConfigureAwait(false);
            IReadOnlyList<SdlGamepadSource> sources = GetSourcesSnapshot();
            if (message.Type == ControllerPipeFrameType.Feedback &&
                message.Feedback.ControllerIndex < sources.Count)
            {
                _ = sources[message.Feedback.ControllerIndex].TrySendFeedback(message.Feedback.Feedback);
            }
        }
    }

    private void SendInput(
        SdlGamepadSource source,
        ControllerState state)
    {
        ControllerPipeWriter? writer = _writer;
        if (writer is null || _stop.IsCancellationRequested)
        {
            return;
        }

        ushort controllerIndex = FindSourceIndex(source);
        lock (_writeGate)
        {
            writer.WriteInputAsync(new ControllerInputFrame(controllerIndex, state))
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }
    }

    private static ClientControllerInfo[] CreateControllerInfos(
        IReadOnlyList<SdlGamepadSource> sources,
        Dictionary<SdlGamepadSource, string> physicalIds)
    {
        ClientControllerInfo[] controllers = new ClientControllerInfo[sources.Count];
        for (int i = 0; i < sources.Count; i++)
        {
            SdlGamepadSource source = sources[i];
            controllers[i] = new ClientControllerInfo(
                checked((ushort)i),
                physicalIds[source],
                source.Features);
        }

        return controllers;
    }

    private static Dictionary<SdlGamepadSource, string> CreatePhysicalIds(
        IReadOnlyList<SdlGamepadSource> sources,
        IReadOnlyList<SdlControllerInfo> physicalControllers)
    {
        Dictionary<SdlGamepadSource, string> ids = [];
        foreach (SdlGamepadSource source in sources)
        {
            SdlControllerInfo controller = source.Controller;
            SdlControllerInfo? physical = SdlControllerMatcher.FindPhysicalController(
                controller,
                physicalControllers);
            ids[source] = SdlControllerCatalog.GetPhysicalControllerId(physical ?? controller);
        }

        return ids;
    }

    private static List<SdlControllerInfo> GetPhysicalControllers(
        IReadOnlyList<SdlControllerInfo> controllers)
    {
        List<SdlControllerInfo> physical = [];
        foreach (SdlControllerInfo controller in controllers)
        {
            if (controller.Source == SdlControllerSource.Physical)
            {
                physical.Add(controller);
            }
        }

        return physical;
    }

    private ushort FindSourceIndex(SdlGamepadSource source)
    {
        IReadOnlyList<SdlGamepadSource> sources = GetSourcesSnapshot();
        for (int i = 0; i < sources.Count; i++)
        {
            if (ReferenceEquals(sources[i], source))
            {
                return checked((ushort)i);
            }
        }

        return 0;
    }

    private async Task<IReadOnlyList<SdlGamepadSource>> RefreshSourcesAsync(
        VirtualMouseClient client,
        CancellationToken cancellationToken)
    {
        await DisposeSourcesAsync().ConfigureAwait(false);

        IReadOnlyList<SdlControllerInfo> visibleControllers =
            SdlControllerCatalog.GetControllers(SdlControllerFilters.IsForwardable);
        List<SdlControllerInfo> physicalControllers = GetPhysicalControllers(visibleControllers);
        IReadOnlyList<SdlGamepadSource> sources =
            SdlControllerCatalog.OpenClientControllers(SdlControllerFilters.IsForwardable);
        try
        {
            Dictionary<SdlGamepadSource, string> physicalIds = CreatePhysicalIds(sources, physicalControllers);
            ClientControllerInfo[] controllers = CreateControllerInfos(sources, physicalIds);

            await client.RegisterClientControllersAsync(controllers, cancellationToken).ConfigureAwait(false);
            lock (_sourcesGate)
            {
                _sources = sources;
            }

            return sources;
        }
        catch
        {
            foreach (SdlGamepadSource source in sources)
            {
                await source.DisposeAsync().ConfigureAwait(false);
            }

            throw;
        }
    }

    private async Task DisposeSourcesAsync()
    {
        IReadOnlyList<SdlGamepadSource> sources;
        lock (_sourcesGate)
        {
            sources = _sources;
            _sources = [];
        }

        foreach (SdlGamepadSource source in sources)
        {
            await source.DisposeAsync().ConfigureAwait(false);
        }
    }

    private IReadOnlyList<SdlGamepadSource> GetSourcesSnapshot()
    {
        lock (_sourcesGate)
        {
            return _sources;
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
            await task.ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is OperationCanceledException or IOException or ObjectDisposedException)
        {
        }
    }
}
