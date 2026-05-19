using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VirtualMouse.Forwarding;
using VirtualMouse.Inputs.Sdl;
using VirtualMouse.Outputs.Viiper;

namespace VirtualMouse.Hosting;

internal static class SdlControllerFilters
{
    public static bool IsForwardable(SdlControllerInfo controller)
    {
        return !ViiperDevices.IsController(controller.VendorId, controller.ProductId);
    }
}

internal sealed class PhysicalControllerPump(
    ControllerBroker broker,
    ILogger logger) : IAsyncDisposable
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    private readonly CancellationTokenSource _stop = new();
    private readonly Lock _gate = new();
    private List<SdlGamepadSource> _sources = [];
    private Task? _task;
    private string? _lastError;
    private bool _running;
    private bool _disposed;

    public void Start(CancellationToken cancellationToken)
    {
        _task = Task.Run(() => RunLinkedAsync(cancellationToken), CancellationToken.None);
    }

    public PhysicalControllerPumpStatus GetStatus()
    {
        lock (_gate)
        {
            List<string> controllerIds = [];
            foreach (SdlGamepadSource source in _sources)
            {
                controllerIds.Add(SdlControllerCatalog.GetPhysicalControllerId(source.Controller));
            }

            return new PhysicalControllerPumpStatus(
                _running,
                _sources.Count,
                controllerIds,
                _lastError);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _stop.CancelAsync().ConfigureAwait(false);
        if (_task is not null)
        {
            try
            {
                await _task.ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException)
            {
            }
        }

        await DisposeSourcesAsync().ConfigureAwait(false);

        _stop.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await DisposeSourcesAsync().ConfigureAwait(false);
            try
            {
                IReadOnlyList<SdlGamepadSource> sources = AddMissingSources();
                lock (_gate)
                {
                    _running = sources.Count != 0;
                    _lastError = null;
                }

                if (sources.Count == 0)
                {
                    await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                HostingLog.PhysicalControllerPumpStarted(logger, sources.Count);
                SdlGamepadEventLoop.Run(
                    GetSourcesSnapshot,
                    UpdatePhysicalController,
                    RemoveSource,
                    () => _ = AddMissingSources(),
                    cancellationToken);
            }
            catch (Exception exception) when (
                exception is SdlGamepadDisconnectedException or InvalidOperationException)
            {
                lock (_gate)
                {
                    _running = false;
                    _lastError = exception.Message;
                }

                HostingLog.PhysicalControllerPumpRestarting(logger, exception.Message);
                await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private IReadOnlyList<SdlGamepadSource> AddMissingSources()
    {
        HashSet<SdlControllerId> openIds = GetOpenSourceIds();
        IReadOnlyList<SdlControllerInfo> controllers =
            SdlControllerCatalog.GetControllers(SdlControllerFilters.IsForwardable);
        List<SdlControllerInfo> missingControllers = [];

        foreach (SdlControllerInfo controller in controllers)
        {
            if (controller.Source == SdlControllerSource.Physical &&
                !openIds.Contains(controller.Id))
            {
                missingControllers.Add(controller);
            }
        }

        IReadOnlyList<SdlGamepadSource> openedSources = OpenSources(missingControllers);
        lock (_gate)
        {
            _sources.AddRange(openedSources);
            return [.. _sources];
        }
    }

    private async Task RunLinkedAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource linked =
            CancellationTokenSource.CreateLinkedTokenSource(_stop.Token, cancellationToken);
        await RunAsync(linked.Token).ConfigureAwait(false);
    }

    private void UpdatePhysicalController(SdlGamepadSource source, ControllerState state)
    {
        broker.UpdatePhysicalController(
            new ControllerId(SdlControllerCatalog.GetPhysicalControllerId(source.Controller), source.Controller.Name),
            state,
            source.Features,
            source);
    }

    private void RemoveSource(SdlGamepadSource source)
    {
        SdlGamepadSource? removed = null;
        lock (_gate)
        {
            List<SdlGamepadSource> sources = [.. _sources];
            if (sources.Remove(source))
            {
                removed = source;
                _sources = sources;
                _running = sources.Count != 0;
            }
        }

        if (removed is null)
        {
            return;
        }

        broker.RemovePhysicalController(new ControllerId(
            SdlControllerCatalog.GetPhysicalControllerId(removed.Controller),
            removed.Controller.Name));
        removed.Dispose();
    }

    private async Task DisposeSourcesAsync()
    {
        IReadOnlyList<SdlGamepadSource> sources;
        lock (_gate)
        {
            sources = _sources;
            _sources = [];
            _running = false;
        }

        foreach (SdlGamepadSource source in sources)
        {
            broker.RemovePhysicalController(new ControllerId(
                SdlControllerCatalog.GetPhysicalControllerId(source.Controller),
                source.Controller.Name));
            await source.DisposeAsync().ConfigureAwait(false);
        }
    }

    private IReadOnlyList<SdlGamepadSource> GetSourcesSnapshot()
    {
        lock (_gate)
        {
            return [.. _sources];
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
}
