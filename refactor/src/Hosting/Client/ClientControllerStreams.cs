using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VirtualMouse.Forwarding;
using VirtualMouse.Inputs.Sdl;

namespace VirtualMouse.Hosting;

internal sealed class ClientControllerStreams(ILogger logger) : IAsyncDisposable
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    private readonly CancellationTokenSource _stop = new();
    private readonly Lock _sourcesGate = new();
    private readonly Channel<ControllerInputFrame> _inputWrites = Channel.CreateBounded<ControllerInputFrame>(
        new BoundedChannelOptions(capacity: 128)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });
    private IReadOnlyList<SdlGamepadSource> _sources = [];
    private NamedPipeClientStream? _pipe;
    private ControllerPipeWriter? _writer;
    private Task? _inputTask;
    private Task? _writeTask;
    private Task? _feedbackTask;

    public async Task StartAsync(
        ClientService client,
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
        _writeTask = Task.Run(RunInputWriteLoopAsync, CancellationToken.None);
        _feedbackTask = Task.Run(() => RunFeedbackLoopAsync(reader), CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        _pipe?.Dispose();
        await IgnoreExpectedStopAsync(_inputTask).ConfigureAwait(false);
        await IgnoreExpectedStopAsync(_writeTask).ConfigureAwait(false);
        await IgnoreExpectedStopAsync(_feedbackTask).ConfigureAwait(false);

        await DisposeSourcesAsync().ConfigureAwait(false);

        _stop.Dispose();
    }

    private async Task RunInputLoopAsync(ClientService client)
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

                SdlGamepadEventLoop.Run(
                    GetSourcesSnapshot,
                    SendInput,
                    source => RemoveSource(client, source),
                    () => RefreshSources(client),
                    _stop.Token);
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

                HostingLog.SdlControllerStreamingRestarting(logger, exception.Message);
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

    private async Task RunInputWriteLoopAsync()
    {
        ControllerPipeWriter writer = _writer ??
            throw new InvalidOperationException("Controller pipe writer is not connected.");

        await foreach (ControllerInputFrame frame in _inputWrites.Reader.ReadAllAsync(_stop.Token)
            .ConfigureAwait(false))
        {
            await writer.WriteInputAsync(frame, _stop.Token).ConfigureAwait(false);
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
        _ = _inputWrites.Writer.TryWrite(new ControllerInputFrame(controllerIndex, state));
    }

    private static ClientControllerInfo[] CreateControllerInfos(
        IReadOnlyList<SdlGamepadSource> sources,
        Dictionary<SdlGamepadSource, ControllerSlotIdentity> identities)
    {
        ClientControllerInfo[] controllers = new ClientControllerInfo[sources.Count];
        for (int i = 0; i < sources.Count; i++)
        {
            SdlGamepadSource source = sources[i];
            ControllerSlotIdentity identity = identities[source];
            controllers[i] = new ClientControllerInfo(
                checked((ushort)i),
                identity.PhysicalId,
                identity.Label,
                source.Features);
        }

        return controllers;
    }

    internal static IReadOnlyList<SdlControllerInfo> SelectClientControllers(
        IReadOnlyList<SdlControllerInfo> visibleControllers)
    {
        List<SdlControllerInfo> physicalControllers = GetPhysicalControllers(visibleControllers);
        HashSet<string> steamMatchedPhysicalIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (SdlControllerInfo controller in visibleControllers)
        {
            if (controller.Source == SdlControllerSource.Steam &&
                SdlControllerMatcher.FindPhysicalController(controller, physicalControllers) is { } physical)
            {
                _ = steamMatchedPhysicalIds.Add(SdlControllerCatalog.GetPhysicalControllerId(physical));
            }
        }

        List<SdlControllerInfo> selected = [];
        foreach (SdlControllerInfo controller in visibleControllers)
        {
            if (controller.Source == SdlControllerSource.Steam ||
                !steamMatchedPhysicalIds.Contains(SdlControllerCatalog.GetPhysicalControllerId(controller)))
            {
                selected.Add(controller);
            }
        }

        return selected;
    }

    private static List<SdlGamepadSource> OpenSources(IReadOnlyList<SdlControllerInfo> controllers)
    {
        List<SdlGamepadSource> sources = [];
        try
        {
            foreach (SdlControllerInfo controller in controllers)
            {
                sources.Add(SdlGamepadSource.Connect(controller));
            }

            return sources;
        }
        catch
        {
            foreach (SdlGamepadSource source in sources)
            {
                source.Dispose();
            }

            throw;
        }
    }

    private static Dictionary<SdlGamepadSource, ControllerSlotIdentity> CreateSlotIdentities(
        IReadOnlyList<SdlGamepadSource> sources,
        IReadOnlyList<SdlControllerInfo> physicalControllers)
    {
        Dictionary<SdlGamepadSource, ControllerSlotIdentity> identities = [];
        foreach (SdlGamepadSource source in sources)
        {
            SdlControllerInfo controller = source.Controller;
            SdlControllerInfo? physical = SdlControllerMatcher.FindPhysicalController(
                controller,
                physicalControllers);
            SdlControllerInfo slot = physical ?? controller;
            identities[source] = new ControllerSlotIdentity(
                SdlControllerCatalog.GetPhysicalControllerId(slot),
                slot.Name);
        }

        return identities;
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
        ClientService client,
        CancellationToken cancellationToken)
    {
        await DisposeSourcesAsync().ConfigureAwait(false);
        await client.RegisterClientControllersAsync([], cancellationToken).ConfigureAwait(false);
        return await AddMissingSourcesAsync(client, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<SdlGamepadSource>> AddMissingSourcesAsync(
        ClientService client,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<SdlControllerInfo> visibleControllers =
            SdlControllerCatalog.GetControllers(SdlControllerFilters.IsForwardable);
        List<SdlControllerInfo> physicalControllers = GetPhysicalControllers(visibleControllers);
        IReadOnlyList<SdlControllerInfo> selectedControllers = SelectClientControllers(visibleControllers);
        HashSet<SdlControllerId> openIds = GetOpenSourceIds();
        List<SdlControllerInfo> missingControllers = [];
        foreach (SdlControllerInfo controller in selectedControllers)
        {
            if (!openIds.Contains(controller.Id))
            {
                missingControllers.Add(controller);
            }
        }

        IReadOnlyList<SdlGamepadSource> openedSources = OpenSources(missingControllers);
        try
        {
            IReadOnlyList<SdlGamepadSource> sources = AddSources(openedSources);
            Dictionary<SdlGamepadSource, ControllerSlotIdentity> identities =
                CreateSlotIdentities(sources, physicalControllers);
            ClientControllerInfo[] controllers = CreateControllerInfos(sources, identities);

            await client.RegisterClientControllersAsync(controllers, cancellationToken).ConfigureAwait(false);
            return sources;
        }
        catch
        {
            foreach (SdlGamepadSource source in openedSources)
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

    private void RemoveSource(ClientService client, SdlGamepadSource source)
    {
        SdlGamepadSource? removed = null;
        lock (_sourcesGate)
        {
            List<SdlGamepadSource> sources = [.. _sources];
            if (sources.Remove(source))
            {
                removed = source;
                _sources = sources;
            }
        }

        if (removed is null)
        {
            return;
        }

        removed.Dispose();
        RefreshControllerRegistration(client);
    }

    private void RefreshSources(ClientService client)
    {
        _ = AddMissingSourcesAsync(client, _stop.Token).GetAwaiter().GetResult();
    }

    private void RefreshControllerRegistration(ClientService client)
    {
        IReadOnlyList<SdlGamepadSource> sources = GetSourcesSnapshot();
        ClientControllerInfo[] controllers = CreateControllerInfos(
            sources,
            CreateSlotIdentities(sources, GetPhysicalControllers(SdlControllerCatalog.GetControllers(
                SdlControllerFilters.IsForwardable))));
        client.RegisterClientControllersAsync(controllers, _stop.Token).GetAwaiter().GetResult();
    }

    private IReadOnlyList<SdlGamepadSource> AddSources(IReadOnlyList<SdlGamepadSource> sources)
    {
        if (sources.Count == 0)
        {
            return GetSourcesSnapshot();
        }

        lock (_sourcesGate)
        {
            _sources = [.. _sources, .. sources];
            return _sources;
        }
    }

    private HashSet<SdlControllerId> GetOpenSourceIds()
    {
        IReadOnlyList<SdlGamepadSource> sources = GetSourcesSnapshot();
        HashSet<SdlControllerId> ids = [];
        foreach (SdlGamepadSource source in sources)
        {
            _ = ids.Add(source.Controller.Id);
        }

        return ids;
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

    private sealed record ControllerSlotIdentity(string PhysicalId, string Label);
}
