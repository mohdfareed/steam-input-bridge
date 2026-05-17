using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Inputs.Sdl;
using Microsoft.Extensions.Logging;

namespace Hosting;

internal sealed class HostedRouteController(
    string routeId,
    Func<CancellationToken, Task<IForwardingRoute>> createRouteAsync,
    ILogger? logger,
    Func<bool>? shouldForward = null) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private HostedRouteInstance? _instance;
    private int _enabledClientCount;

    public async Task<IDisposable> EnableAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            _instance ??= await HostedRouteInstance.StartAsync(
                    createRouteAsync,
                    logger,
                    shouldForward,
                    cancellationToken)
                .ConfigureAwait(false);

            _enabledClientCount++;
            return new HostedRouteLease(Release);
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    public async Task<ForwardingRouteStatus> GetStatusAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);

        try
        {
            return new ForwardingRouteStatus(
                routeId,
                _instance?.Host.IsConnected ?? false,
                _enabledClientCount);
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        HostedRouteInstance? instance;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _enabledClientCount = 0;
            instance = _instance;
            _instance = null;
        }
        finally
        {
            _ = _gate.Release();
        }

        if (instance is not null)
        {
            await instance.DisposeAsync().ConfigureAwait(false);
        }

        _gate.Dispose();
    }

    private void Release()
    {
        ReleaseAsync().AsTask().GetAwaiter().GetResult();
    }

    private async ValueTask ReleaseAsync()
    {
        HostedRouteInstance? instanceToStop = null;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _enabledClientCount -= 1;
            if (_enabledClientCount < 0)
            {
                _enabledClientCount = 0;
                throw new InvalidOperationException("Route enable leases are unbalanced.");
            }

            if (_enabledClientCount == 0)
            {
                instanceToStop = _instance;
                _instance = null;
            }
        }
        finally
        {
            _ = _gate.Release();
        }

        if (instanceToStop is not null)
        {
            await instanceToStop.DisposeAsync().ConfigureAwait(false);
        }
    }

    private sealed class HostedRouteLease(Action release) : IDisposable
    {
        private Action? _release = release;

        public void Dispose()
        {
            Action? current = Interlocked.Exchange(ref _release, null);
            current?.Invoke();
        }
    }

    private sealed class HostedRouteInstance(
        ForwardingHost host,
        IDisposable enableLease,
        CancellationTokenSource cancellationSource,
        Task runTask) : IAsyncDisposable
    {
        public ForwardingHost Host { get; } = host;

        public static async Task<HostedRouteInstance> StartAsync(
            Func<CancellationToken, Task<IForwardingRoute>> createRouteAsync,
            ILogger? logger,
            Func<bool>? shouldForward,
            CancellationToken cancellationToken)
        {
#pragma warning disable CA2000
            IForwardingRoute route = await createRouteAsync(cancellationToken).ConfigureAwait(false);
            ForwardingHost host = new(route, logger, shouldForward);

            try
            {
                IDisposable enableLease = host.Enable();

                try
                {
                    CancellationTokenSource runCancellation = new();

                    try
                    {
                        Task runTask = Task.Run(() => host.Run(runCancellation.Token), CancellationToken.None);
                        return new HostedRouteInstance(host, enableLease, runCancellation, runTask);
                    }
                    catch
                    {
                        runCancellation.Dispose();
                        throw;
                    }
                }
                catch
                {
                    enableLease.Dispose();
                    throw;
                }
            }
            catch
            {
                await host.DisposeAsync().ConfigureAwait(false);
                throw;
            }
#pragma warning restore CA2000
        }

        public async ValueTask DisposeAsync()
        {
            await cancellationSource.CancelAsync().ConfigureAwait(false);

            try
            {
                await runTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
            {
            }
            catch (IOException) when (cancellationSource.IsCancellationRequested)
            {
            }
            catch (ObjectDisposedException) when (cancellationSource.IsCancellationRequested)
            {
            }
            catch (InvalidOperationException) when (cancellationSource.IsCancellationRequested)
            {
            }
            finally
            {
                enableLease.Dispose();
                cancellationSource.Dispose();
                await Host.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}

internal sealed class ForwardingHostRuntime(
    HostedRouteController mouse,
    GamepadControllerRegistry gamepads,
    ForwardingHostState hostState) : IAsyncDisposable
{
    public Task<ForwardingHostStatus> GetStatusAsync()
    {
        return GetStatusCoreAsync();
    }

    public Task<IDisposable> EnableMouseAsync(CancellationToken cancellationToken)
    {
        return mouse.EnableAsync(cancellationToken);
    }

    public Task<GamepadReportSessionInfo> AttachSteamControllerAsync(
        SdlControllerInfo controller,
        CancellationToken cancellationToken)
    {
        return gamepads.AttachSteamControllerAsync(controller, cancellationToken);
    }

    public Task DetachSteamControllerAsync(Guid sessionId)
    {
        return gamepads.DetachAsync(sessionId);
    }

    public Task SetEmulationEnabledAsync(bool enabled)
    {
        hostState.SetEmulationEnabled(enabled);
        return Task.CompletedTask;
    }

    public Task<bool> ToggleEmulationEnabledAsync()
    {
        return Task.FromResult(hostState.ToggleEmulationEnabled());
    }

    public Task SetPhysicalMotionEnabledAsync(bool enabled)
    {
        hostState.SetPhysicalMotionEnabled(enabled);
        return Task.CompletedTask;
    }

    public Task<bool> TogglePhysicalMotionEnabledAsync()
    {
        return Task.FromResult(hostState.TogglePhysicalMotionEnabled());
    }

    public async ValueTask DisposeAsync()
    {
        await mouse.DisposeAsync().ConfigureAwait(false);
        await gamepads.DisposeAsync().ConfigureAwait(false);
    }

    private async Task<ForwardingHostStatus> GetStatusCoreAsync()
    {
        ForwardingRouteStatus mouseStatus = await mouse.GetStatusAsync().ConfigureAwait(false);
        IReadOnlyList<GamepadControllerSlotStatus> gamepadStatuses =
            await gamepads.GetStatusAsync().ConfigureAwait(false);

        return new ForwardingHostStatus(
            mouseStatus,
            gamepadStatuses,
            hostState.EmulationEnabled,
            hostState.PhysicalMotionEnabled);
    }
}
