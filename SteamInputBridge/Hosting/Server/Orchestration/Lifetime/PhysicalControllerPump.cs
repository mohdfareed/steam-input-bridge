using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Forwarding.Controller;
using SteamInputBridge.Forwarding.Controller.Routing;
using SteamInputBridge.Inputs.Sdl;

namespace SteamInputBridge.Hosting.Server.Orchestration.Lifetime;

internal interface IPhysicalControllerResolver
{
    void SetClientControllers(Guid clientId, IReadOnlyList<ClientControllerInfo> controllers);

    ClientControllerInfo? ResolveClientController(Guid clientId, ClientControllerInfo controller);

    void ObserveClientControllerInput(Guid clientId, ushort controllerIndex, ControllerState state);

    void RemoveClient(Guid clientId);
}

internal sealed partial class PhysicalControllerPump(
    ControllerBroker broker,
    ILogger logger,
    ServerControllerInputFilter? inputFilter = null) : IPhysicalControllerResolver, IAsyncDisposable
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);
    private readonly Lock _gate = new();
    private readonly Dictionary<SdlControllerId, SdlGamepadSource> _sources = [];
    private readonly ServerControllerInputFilter? _inputFilter = inputFilter;
    private CancellationTokenSource? _stop;
    private Task? _task;
    private bool _running;
    private bool _disposed;
    private string? _lastError;

    public event Action? ControllersChanged;

    // MARK: Publics
    // ========================================================================

    public void Start(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_task is not null)
            {
                return;
            }

            _stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _running = true;
            _task = Task.Run(() => RunAsync(_stop.Token), CancellationToken.None);
        }
    }

    public ControllerInputPumpStatus GetStatus()
    {
        lock (_gate)
        {
            return new ControllerInputPumpStatus(_running, _sources.Count, _lastError);
        }
    }

    public void SetClientControllers(Guid clientId, IReadOnlyList<ClientControllerInfo> controllers)
    {
        ArgumentNullException.ThrowIfNull(controllers);

        HashSet<ControllerMatchKey> current = [];
        foreach (ClientControllerInfo controller in controllers)
        {
            _ = current.Add(new ControllerMatchKey(clientId, controller.ControllerIndex));
        }

        lock (_gate)
        {
            RemoveClientMatchesExcept(clientId, current, controllers);
            TrackClientControllerBatchNoLock(clientId, controllers);
        }
    }

    public ClientControllerInfo? ResolveClientController(Guid clientId, ClientControllerInfo controller)
    {
        ArgumentNullException.ThrowIfNull(controller);

        ServerControllerInputFilterSnapshot? filter = _inputFilter?.CreateSnapshot();
        if (filter?.Allows(controller) == false)
        {
            return null;
        }

        if (!IsSteamRoute(controller.PhysicalControllerId) ||
            !string.IsNullOrWhiteSpace(controller.PhysicalDeviceId))
        {
            return GetControllerPathId(controller.PhysicalDeviceId) is null &&
                GetControllerPathId(controller.PhysicalControllerId) is null
                ? controller
                : FindKnownPhysicalController(controller) is { } knownPhysical
                ? controller with
                {
                    PhysicalControllerId = SdlControllerRoutePolicy.GetPhysicalControllerId(knownPhysical),
                    Label = knownPhysical.Name,
                    PhysicalDeviceId = GetPathControllerId(knownPhysical),
                }
                : null;
        }

        if (TryResolveActivityMatch(clientId, controller, out ClientControllerInfo resolved))
        {
            return resolved;
        }

        if (CanUseClientOnlyOutput(clientId, controller))
        {
            return controller;
        }

        TrackPendingMatch(clientId, controller);
        return null;
    }

    public void ObserveClientControllerInput(Guid clientId, ushort controllerIndex, ControllerState state)
    {
        if (ObserveClientMatchInput(clientId, controllerIndex, state))
        {
            ControllersChanged?.Invoke();
        }
    }

    public void RemoveClient(Guid clientId)
    {
        lock (_gate)
        {
            RemoveClientMatches(clientId);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task? task;
        CancellationTokenSource? stop;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            task = _task;
            stop = _stop;
        }

        if (stop is not null)
        {
            await stop.CancelAsync().ConfigureAwait(false);
        }

        await IgnoreExpectedStopAsync(task).ConfigureAwait(false);
        await ClearSourcesAsync().ConfigureAwait(false);
        stop?.Dispose();
    }

    // MARK: Loop
    // ========================================================================

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        HostingLog.PhysicalControllerPumpStarted(logger);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    IReadOnlyList<SdlGamepadSource> sources = RefreshSources();
                    if (sources.Count == 0)
                    {
                        await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    SdlGamepadEventLoop.Run(
                        GetSourcesSnapshot,
                        UpdateSource,
                        RemoveSource,
                        () => _ = RefreshSources(),
                        cancellationToken);
                }
                catch (Exception exception) when (
                    exception is SdlGamepadDisconnectedException or
                        InvalidOperationException or
                        IOException or
                        ObjectDisposedException)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    SetLastError(exception.Message);
                    HostingLog.PhysicalControllerPumpRestarting(logger, exception.Message);
                    await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            lock (_gate)
            {
                _running = false;
            }
        }
    }

    private IReadOnlyList<SdlGamepadSource> RefreshSources()
    {
        IReadOnlyList<SdlGamepadSource> openedSources = SdlControllerCatalog.OpenControllers(controllers =>
        {
            List<SdlControllerInfo> physicalControllers = SelectPhysicalControllers(controllers);
            RemoveStaleSources(physicalControllers);
            HashSet<SdlControllerId> openIds = GetOpenSourceIds();
            List<SdlControllerInfo> missing = [];
            foreach (SdlControllerInfo controller in physicalControllers)
            {
                if (!openIds.Contains(controller.Id))
                {
                    missing.Add(controller);
                }
            }

            return missing;
        });

        AddSources(openedSources);
        return GetSourcesSnapshot();
    }

    private void AddSources(IReadOnlyList<SdlGamepadSource> openedSources)
    {
        if (openedSources.Count == 0)
        {
            return;
        }

        lock (_gate)
        {
            foreach (SdlGamepadSource source in openedSources)
            {
                _sources[source.Controller.Id] = source;
                ControllerState state = source.ReadCurrentState();
                UpdateBroker(source, state);
                InitializePhysicalActivityNoLock(source.Controller, state);
            }

            _lastError = null;
        }

        ControllersChanged?.Invoke();
    }

    private void RemoveStaleSources(IReadOnlyList<SdlControllerInfo> physicalControllers)
    {
        ServerControllerInputFilterSnapshot? filter = _inputFilter?.CreateSnapshot();
        Dictionary<SdlControllerId, SdlControllerInfo> current = [];
        foreach (SdlControllerInfo controller in physicalControllers)
        {
            current[controller.Id] = controller;
        }

        List<SdlGamepadSource> removed = [];
        lock (_gate)
        {
            foreach (KeyValuePair<SdlControllerId, SdlGamepadSource> entry in _sources)
            {
                if (!current.TryGetValue(entry.Key, out SdlControllerInfo? controller))
                {
                    // SDL scans can be empty or partial while Windows, Steam,
                    // HidHide, or VIIPER rebuild device visibility. A missing
                    // scan entry is not a disconnect; the open source will
                    // emit GamepadRemoved if the physical device is really gone.
                    continue;
                }

                if (SdlControllerRoutePolicy.IsSameConnectedController(entry.Value.Controller, controller))
                {
                    continue;
                }

                // HidHide can briefly change a scoped controller's visible
                // identity while the profile is being hidden from the game.
                // Keep the already-open source until SDL reports a real
                // disconnect.
                if (filter?.IsCurrentScopeDevice(entry.Value.Controller) == true)
                {
                    continue;
                }

                removed.Add(entry.Value);
            }

            foreach (SdlGamepadSource source in removed)
            {
                _ = _sources.Remove(source.Controller.Id);
                RemovePhysicalActivityNoLock(source.Controller);
                broker.RemovePhysicalController(GetControllerId(source.Controller));
            }
        }

        DisposeSources(removed);
        if (removed.Count != 0)
        {
            ControllersChanged?.Invoke();
        }
    }

    private void RemoveSource(SdlGamepadSource source)
    {
        bool keepRoute = _inputFilter?.CreateSnapshot().IsCurrentScopeDevice(source.Controller) == true;
        lock (_gate)
        {
            _ = _sources.Remove(source.Controller.Id);
            RemovePhysicalActivityNoLock(source.Controller);
            if (keepRoute)
            {
                broker.UpdatePhysicalController(
                    GetControllerId(source.Controller),
                    ControllerState.Empty,
                    source.Features);
            }
            else
            {
                broker.RemovePhysicalController(GetControllerId(source.Controller));
            }
        }

        source.Dispose();
        if (keepRoute)
        {
            _ = RefreshSources();
        }

        ControllersChanged?.Invoke();
    }

    private void UpdateSource(SdlGamepadSource source, ControllerState state)
    {
        lock (_gate)
        {
            UpdateBroker(source, state);
        }

        if (ObservePhysicalMatchInput(source.Controller, state))
        {
            ControllersChanged?.Invoke();
        }
    }

    private void UpdateBroker(SdlGamepadSource source, ControllerState state)
    {
        broker.UpdatePhysicalController(
            GetControllerId(source.Controller),
            state,
            source.Features,
            source);
    }

    // MARK: Matching
    // ========================================================================

    private SdlControllerInfo? FindKnownPhysicalController(ClientControllerInfo controller)
    {
        string? pathId = GetControllerPathId(controller.PhysicalDeviceId) ??
            GetControllerPathId(controller.PhysicalControllerId);
        if (pathId is null)
        {
            return null;
        }

        lock (_gate)
        {
            foreach (SdlGamepadSource source in _sources.Values)
            {
                if (string.Equals(
                        SdlControllerRoutePolicy.GetPhysicalControllerId(source.Controller),
                        pathId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return source.Controller;
                }
            }
        }

        return null;
    }

    private HashSet<SdlControllerId> GetOpenSourceIds()
    {
        lock (_gate)
        {
            return [.. _sources.Keys];
        }
    }

    private IReadOnlyList<SdlGamepadSource> GetSourcesSnapshot()
    {
        lock (_gate)
        {
            return [.. _sources.Values];
        }
    }

    private async Task ClearSourcesAsync()
    {
        List<SdlGamepadSource> sources;
        lock (_gate)
        {
            sources = [.. _sources.Values];
            _sources.Clear();
            foreach (SdlGamepadSource source in sources)
            {
                RemovePhysicalActivityNoLock(source.Controller);
                broker.RemovePhysicalController(GetControllerId(source.Controller));
            }
        }

        foreach (SdlGamepadSource source in sources)
        {
            await source.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void SetLastError(string? error)
    {
        lock (_gate)
        {
            _lastError = error;
        }
    }

    private List<SdlControllerInfo> SelectPhysicalControllers(
        IReadOnlyList<SdlControllerInfo> controllers)
    {
        _inputFilter?.Observe(controllers);
        ServerControllerInputFilterSnapshot? filter = _inputFilter?.CreateSnapshot();
        List<SdlControllerInfo> physicalControllers = [];
        foreach (SdlControllerInfo controller in controllers)
        {
            if (controller.Source == SdlControllerSource.Physical &&
                SdlControllerRoutePolicy.IsForwardable(controller) &&
                filter?.Allows(controller) != false)
            {
                physicalControllers.Add(controller);
            }
        }

        return physicalControllers;
    }

    private static ControllerId GetControllerId(SdlControllerInfo controller)
    {
        return new ControllerId(SdlControllerRoutePolicy.GetPhysicalControllerId(controller), controller.Name);
    }

    private static string? GetPathControllerId(SdlControllerInfo controller)
    {
        return string.IsNullOrWhiteSpace(controller.Path)
            ? null
            : SdlControllerRoutePolicy.GetPhysicalControllerId(controller);
    }

    private static string? GetControllerPathId(string? controllerId)
    {
        return !string.IsNullOrWhiteSpace(controllerId) &&
            controllerId.StartsWith("path:", StringComparison.OrdinalIgnoreCase)
            ? controllerId
            : null;
    }

    private static bool IsSteamRoute(string routeId)
    {
        return routeId.StartsWith("steam:", StringComparison.OrdinalIgnoreCase);
    }

    private static void DisposeSources(IReadOnlyList<SdlGamepadSource> sources)
    {
        foreach (SdlGamepadSource source in sources)
        {
            source.Dispose();
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
