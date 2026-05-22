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
    private IReadOnlyList<ClientControllerRouteSource> _sources = [];
    private ushort _nextControllerIndex;
    private NamedPipeClientStream? _pipe;
    private ControllerPipeWriter? _writer;
    private Task? _inputTask;
    private Task? _writeTask;
    private Task? _feedbackTask;
    private string? _lastScanSignature;
    private string? _lastRouteSignature;

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

        _inputTask = Task.Run(() => RunInputLoopAsync(client, launch.ProfileId), CancellationToken.None);
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

    private async Task RunInputLoopAsync(ClientService client, string profileId)
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                IReadOnlyList<SdlGamepadSource> sources = await RefreshSourcesAsync(
                        client,
                        profileId,
                        _stop.Token)
                    .ConfigureAwait(false);
                if (sources.Count == 0)
                {
                    await Task.Delay(RetryDelay, _stop.Token).ConfigureAwait(false);
                    continue;
                }

                SdlGamepadEventLoop.Run(
                    GetGamepadSourcesSnapshot,
                    SendInput,
                    source => RemoveSource(client, profileId, source),
                    () => RefreshSources(client, profileId),
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
            IReadOnlyList<ClientControllerRouteSource> sources = GetSourcesSnapshot();
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

    private ushort FindSourceIndex(SdlGamepadSource source)
    {
        IReadOnlyList<ClientControllerRouteSource> sources = GetSourcesSnapshot();
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
        string profileId,
        CancellationToken cancellationToken)
    {
        await DisposeSourcesAsync().ConfigureAwait(false);
        await client.RegisterClientControllersAsync([], cancellationToken).ConfigureAwait(false);
        return await AddMissingSourcesAsync(client, profileId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<SdlGamepadSource>> AddMissingSourcesAsync(
        ClientService client,
        string profileId,
        CancellationToken cancellationToken)
    {
        HashSet<SdlControllerId> openIds = GetOpenSourceIds();
        IReadOnlyList<SdlControllerInfo> visibleControllers = [];
        IReadOnlyList<SdlControllerInfo> selectedControllers = [];
        List<SdlControllerInfo> physicalControllers = [];
        IReadOnlyList<SdlGamepadSource> openedSources = SdlControllerCatalog.OpenControllers(controllers =>
        {
            visibleControllers = ClientControllerRoutePlanner.FilterForwardable(controllers);
            physicalControllers = ClientControllerRoutePlanner.GetPhysicalControllers(visibleControllers);
            selectedControllers = ClientControllerRoutePlanner.SelectClientControllers(visibleControllers);
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
            IReadOnlyList<ClientControllerRouteSource> sources = AddSources(openedSources);
            ClientControllerRoutePlan plan = ClientControllerRoutePlanner.CreatePlan(
                sources,
                physicalControllers);
            LogRoutesIfChanged(client.ClientId, profileId, sources, plan);

            await client.RegisterClientControllersAsync(plan.Controllers, cancellationToken).ConfigureAwait(false);
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

    private void LogScanIfChanged(
        IReadOnlyList<SdlControllerInfo> visibleControllers,
        IReadOnlyList<SdlControllerInfo> selectedControllers,
        IReadOnlyList<SdlGamepadSource> openedSources)
    {
        string signature = ClientControllerRoutePlanner.FormatScanSignature(visibleControllers);
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

    private void LogRoutesIfChanged(
        Guid? clientId,
        string profileId,
        IReadOnlyList<ClientControllerRouteSource> sources,
        ClientControllerRoutePlan plan)
    {
        string routes = ClientControllerRoutePlanner.FormatRouteDecisions(sources, plan.Identities);
        string signature = $"{clientId}:{profileId}:{routes}";
        if (signature == _lastRouteSignature)
        {
            return;
        }

        _lastRouteSignature = signature;
        HostingLog.ClientControllerRoutes(logger, clientId, profileId, routes);
    }

    private async Task DisposeSourcesAsync()
    {
        IReadOnlyList<ClientControllerRouteSource> sources;
        lock (_sourcesGate)
        {
            sources = _sources;
            _sources = [];
        }

        foreach (ClientControllerRouteSource source in sources)
        {
            await source.Source.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void RemoveSource(ClientService client, string profileId, SdlGamepadSource source)
    {
        ClientControllerRouteSource removed = default;
        bool hasRemoved = false;
        lock (_sourcesGate)
        {
            List<ClientControllerRouteSource> sources = [.. _sources];
            int index = sources.FindIndex(entry => ReferenceEquals(entry.Source, source));
            if (index >= 0)
            {
                removed = sources[index];
                hasRemoved = true;
                sources.RemoveAt(index);
                _sources = sources;
            }
        }

        if (!hasRemoved)
        {
            return;
        }

        removed.Source.Dispose();
        RefreshControllerRegistration(client, profileId);
    }

    private void RefreshSources(ClientService client, string profileId)
    {
        _ = AddMissingSourcesAsync(client, profileId, _stop.Token).GetAwaiter().GetResult();
    }

    private void RefreshControllerRegistration(ClientService client, string profileId)
    {
        IReadOnlyList<ClientControllerRouteSource> sources = GetSourcesSnapshot();
        ClientControllerRoutePlan plan = ClientControllerRoutePlanner.CreatePlan(
            sources,
            ClientControllerRoutePlanner.GetPhysicalControllers(
                ClientControllerRoutePlanner.FilterForwardable(SdlControllerCatalog.GetControllers())));
        LogRoutesIfChanged(client.ClientId, profileId, sources, plan);
        client.RegisterClientControllersAsync(plan.Controllers, _stop.Token).GetAwaiter().GetResult();
    }

    private IReadOnlyList<ClientControllerRouteSource> AddSources(IReadOnlyList<SdlGamepadSource> sources)
    {
        if (sources.Count == 0)
        {
            return GetSourcesSnapshot();
        }

        lock (_sourcesGate)
        {
            List<ClientControllerRouteSource> entries = [.. _sources];
            foreach (SdlGamepadSource source in sources)
            {
                entries.Add(new ClientControllerRouteSource(_nextControllerIndex++, source));
            }

            _sources = entries;
            return _sources;
        }
    }

    private HashSet<SdlControllerId> GetOpenSourceIds()
    {
        IReadOnlyList<ClientControllerRouteSource> sources = GetSourcesSnapshot();
        HashSet<SdlControllerId> ids = [];
        foreach (ClientControllerRouteSource source in sources)
        {
            _ = ids.Add(source.Source.Controller.Id);
        }

        return ids;
    }

    private IReadOnlyList<SdlGamepadSource> GetGamepadSourcesSnapshot()
    {
        IReadOnlyList<ClientControllerRouteSource> sources = GetSourcesSnapshot();
        SdlGamepadSource[] gamepads = new SdlGamepadSource[sources.Count];
        for (int i = 0; i < sources.Count; i++)
        {
            gamepads[i] = sources[i].Source;
        }

        return gamepads;
    }

    private IReadOnlyList<ClientControllerRouteSource> GetSourcesSnapshot()
    {
        lock (_sourcesGate)
        {
            return _sources;
        }
    }

    private static bool TryGetSource(
        ushort controllerIndex,
        IReadOnlyList<ClientControllerRouteSource> sources,
        out SdlGamepadSource source)
    {
        foreach (ClientControllerRouteSource entry in sources)
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
}
