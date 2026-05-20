using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Forwarding;
using SteamInputBridge.Forwarding.Controller;
using SteamInputBridge.Hosting.Server.Inputs;
using SteamInputBridge.Inputs.Sdl;

namespace SteamInputBridge.Hosting.Client;

internal sealed class ClientControllerStreams(ILogger logger) : IAsyncDisposable
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

    private readonly CancellationTokenSource _stop = new();
    private readonly Lock _sourcesGate = new();
    private readonly Channel<ControllerInputFrame> _inputWrites = Channel.CreateBounded<ControllerInputFrame>(
        new BoundedChannelOptions(capacity: 128)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });
    private IReadOnlyList<ClientControllerSource> _sources = [];
    private ushort _nextControllerIndex;
    private NamedPipeClientStream? _pipe;
    private ControllerPipeWriter? _writer;
    private Task? _inputTask;
    private Task? _writeTask;
    private Task? _feedbackTask;
    private string? _lastScanSignature;

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
                    GetGamepadSourcesSnapshot,
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
            IReadOnlyList<ClientControllerSource> sources = GetSourcesSnapshot();
            if (message.Type == ControllerPipeFrameType.Feedback &&
                TryGetSource(message.Feedback.ControllerIndex, sources, out SdlGamepadSource? source))
            {
                _ = source.TrySendFeedback(message.Feedback.Feedback);
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
        IReadOnlyList<ClientControllerSource> sources,
        Dictionary<SdlGamepadSource, ControllerSlotIdentity> identities)
    {
        ClientControllerInfo[] controllers = new ClientControllerInfo[sources.Count];
        for (int i = 0; i < sources.Count; i++)
        {
            ClientControllerSource source = sources[i];
            SdlGamepadSource gamepad = source.Source;
            ControllerSlotIdentity identity = identities[gamepad];
            controllers[i] = new ClientControllerInfo(
                source.ControllerIndex,
                identity.RouteId,
                identity.Label,
                gamepad.Features,
                identity.PhysicalDeviceId);
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

    private static Dictionary<SdlGamepadSource, ControllerSlotIdentity> CreateSlotIdentities(
        IReadOnlyList<ClientControllerSource> sources,
        IReadOnlyList<SdlControllerInfo> physicalControllers)
    {
        Dictionary<SdlGamepadSource, ControllerSlotIdentity> identities = [];
        foreach (ClientControllerSource source in sources)
        {
            SdlControllerInfo controller = source.Source.Controller;
            SdlControllerInfo? physical = SdlControllerMatcher.FindPhysicalController(
                controller,
                physicalControllers);
            string? physicalDeviceId = physical is not null
                ? GetPathControllerId(physical)
                : GetPathControllerId(controller);
            identities[source.Source] = new ControllerSlotIdentity(
                GetRouteId(source, physical),
                physical?.Name ?? controller.Name,
                physicalDeviceId);
        }

        return identities;
    }

    private static string GetRouteId(ClientControllerSource source, SdlControllerInfo? physical)
    {
        SdlControllerInfo controller = source.Source.Controller;
        return physical is not null
            ? SdlControllerCatalog.GetPhysicalControllerId(physical)
            : !string.IsNullOrWhiteSpace(controller.Path)
            ? SdlControllerCatalog.GetPhysicalControllerId(controller)
            : controller.Source == SdlControllerSource.Steam && controller.SteamHandle != 0
            ? controller.Id.Value
            : string.Create(
                CultureInfo.InvariantCulture,
                $"client:{source.ControllerIndex}:{controller.Source}:{controller.InstanceId}");
    }

    private static string? GetPathControllerId(SdlControllerInfo controller)
    {
        return string.IsNullOrWhiteSpace(controller.Path)
            ? null
            : SdlControllerCatalog.GetPhysicalControllerId(controller);
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
        IReadOnlyList<ClientControllerSource> sources = GetSourcesSnapshot();
        for (int i = 0; i < sources.Count; i++)
        {
            if (ReferenceEquals(sources[i].Source, source))
            {
                return sources[i].ControllerIndex;
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
        HashSet<SdlControllerId> openIds = GetOpenSourceIds();
        IReadOnlyList<SdlControllerInfo> visibleControllers = [];
        IReadOnlyList<SdlControllerInfo> selectedControllers = [];
        List<SdlControllerInfo> physicalControllers = [];
        IReadOnlyList<SdlGamepadSource> openedSources = SdlControllerCatalog.OpenControllers(controllers =>
        {
            visibleControllers = FilterForwardable(controllers);
            physicalControllers = GetPhysicalControllers(visibleControllers);
            selectedControllers = SelectClientControllers(visibleControllers);
            List<SdlControllerInfo> missingControllers = [];
            foreach (SdlControllerInfo controller in selectedControllers)
            {
                if (!openIds.Contains(controller.Id))
                {
                    missingControllers.Add(controller);
                }
            }

            return missingControllers;
        });
        LogScanIfChanged(visibleControllers, selectedControllers, openedSources);
        try
        {
            IReadOnlyList<ClientControllerSource> sources = AddSources(openedSources);
            Dictionary<SdlGamepadSource, ControllerSlotIdentity> identities =
                CreateSlotIdentities(sources, physicalControllers);
            ClientControllerInfo[] controllers = CreateControllerInfos(sources, identities);

            await client.RegisterClientControllersAsync(controllers, cancellationToken).ConfigureAwait(false);
            return GetGamepadSourcesSnapshot();
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

    private static List<SdlControllerInfo> FilterForwardable(
        IReadOnlyList<SdlControllerInfo> controllers)
    {
        List<SdlControllerInfo> forwardable = [];
        foreach (SdlControllerInfo controller in controllers)
        {
            if (SdlControllerFilters.IsForwardable(controller))
            {
                forwardable.Add(controller);
            }
        }

        return forwardable;
    }

    private void LogScanIfChanged(
        IReadOnlyList<SdlControllerInfo> visibleControllers,
        IReadOnlyList<SdlControllerInfo> selectedControllers,
        IReadOnlyList<SdlGamepadSource> openedSources)
    {
        string signature = CreateScanSignature(visibleControllers);
        if (signature == _lastScanSignature)
        {
            return;
        }

        _lastScanSignature = signature;
        HostingLog.ClientControllerScan(
            logger,
            visibleControllers.Count,
            selectedControllers.Count,
            openedSources.Count,
            signature);
    }

    private static string CreateScanSignature(IReadOnlyList<SdlControllerInfo> controllers)
    {
        if (controllers.Count == 0)
        {
            return "none";
        }

        List<string> values = [];
        foreach (SdlControllerInfo controller in controllers)
        {
            values.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{controller.Name} source={controller.Source} vid={controller.VendorId:x4} pid={controller.ProductId:x4} motion={controller.HasMotion}"));
        }

        return string.Join("; ", values);
    }

    private async Task DisposeSourcesAsync()
    {
        IReadOnlyList<ClientControllerSource> sources;
        lock (_sourcesGate)
        {
            sources = _sources;
            _sources = [];
        }

        foreach (ClientControllerSource source in sources)
        {
            await source.Source.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void RemoveSource(ClientService client, SdlGamepadSource source)
    {
        ClientControllerSource? removed = null;
        lock (_sourcesGate)
        {
            List<ClientControllerSource> sources = [.. _sources];
            int index = sources.FindIndex(entry => ReferenceEquals(entry.Source, source));
            if (index >= 0)
            {
                removed = sources[index];
                sources.RemoveAt(index);
                _sources = sources;
            }
        }

        if (removed is null)
        {
            return;
        }

        removed.Source.Dispose();
        RefreshControllerRegistration(client);
    }

    private void RefreshSources(ClientService client)
    {
        _ = AddMissingSourcesAsync(client, _stop.Token).GetAwaiter().GetResult();
    }

    private void RefreshControllerRegistration(ClientService client)
    {
        IReadOnlyList<ClientControllerSource> sources = GetSourcesSnapshot();
        ClientControllerInfo[] controllers = CreateControllerInfos(
            sources,
            CreateSlotIdentities(sources, GetPhysicalControllers(SdlControllerCatalog.GetControllers(
                SdlControllerFilters.IsForwardable))));
        client.RegisterClientControllersAsync(controllers, _stop.Token).GetAwaiter().GetResult();
    }

    private IReadOnlyList<ClientControllerSource> AddSources(IReadOnlyList<SdlGamepadSource> sources)
    {
        if (sources.Count == 0)
        {
            return GetSourcesSnapshot();
        }

        lock (_sourcesGate)
        {
            List<ClientControllerSource> entries = [.. _sources];
            foreach (SdlGamepadSource source in sources)
            {
                entries.Add(new ClientControllerSource(_nextControllerIndex++, source));
            }

            _sources = entries;
            return _sources;
        }
    }

    private HashSet<SdlControllerId> GetOpenSourceIds()
    {
        IReadOnlyList<ClientControllerSource> sources = GetSourcesSnapshot();
        HashSet<SdlControllerId> ids = [];
        foreach (ClientControllerSource source in sources)
        {
            _ = ids.Add(source.Source.Controller.Id);
        }

        return ids;
    }

    private IReadOnlyList<SdlGamepadSource> GetGamepadSourcesSnapshot()
    {
        IReadOnlyList<ClientControllerSource> sources = GetSourcesSnapshot();
        SdlGamepadSource[] gamepads = new SdlGamepadSource[sources.Count];
        for (int i = 0; i < sources.Count; i++)
        {
            gamepads[i] = sources[i].Source;
        }

        return gamepads;
    }

    private IReadOnlyList<ClientControllerSource> GetSourcesSnapshot()
    {
        lock (_sourcesGate)
        {
            return _sources;
        }
    }

    private static bool TryGetSource(
        ushort controllerIndex,
        IReadOnlyList<ClientControllerSource> sources,
        out SdlGamepadSource source)
    {
        foreach (ClientControllerSource entry in sources)
        {
            if (entry.ControllerIndex == controllerIndex)
            {
                source = entry.Source;
                return true;
            }
        }

        source = null!;
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

    private sealed record ClientControllerSource(ushort ControllerIndex, SdlGamepadSource Source);

    private sealed record ControllerSlotIdentity(
        string RouteId,
        string Label,
        string? PhysicalDeviceId);
}
