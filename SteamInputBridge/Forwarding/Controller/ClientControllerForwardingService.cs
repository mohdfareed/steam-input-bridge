using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SDL3;
using SteamInputBridge.Hosting;
using SteamInputBridge.Hosting.Client;
using SteamInputBridge.Inputs.Controller;
using SteamInputBridge.Inputs.Sdl;
using SteamInputBridge.Outputs.Controller;
using SteamInputBridge.Outputs.Viiper.Controller;
using SteamInputBridge.Settings;

namespace SteamInputBridge.Forwarding.Controller;

/// <summary>Client-side Steam Controller to VIIPER Xbox forwarding.</summary>
public sealed class ClientControllerForwardingService(
    ClientRunOptions options,
    SettingsService settings,
    ViiperControllerOutputFactory outputFactory,
    ILogger<ClientControllerForwardingService> logger) : BackgroundService, IAsyncDisposable
{
    private static readonly TimeSpan EventWaitTimeout = TimeSpan.FromMilliseconds(100);

    private readonly Lock _gate = new();
    private readonly Dictionary<ulong, ControllerRoute> _routes = [];
    private bool _active;

    // MARK: Publics
    // ========================================================================

    /// <summary>Sets whether controller input is currently allowed through.</summary>
    public void SetActive(bool active)
    {
        List<IControllerOutput>? clear = null;
        lock (_gate)
        {
            if (_active == active)
            {
                return;
            }

            _active = active;
            if (!active)
            {
                clear = [];
                foreach (ControllerRoute route in _routes.Values)
                {
                    clear.Add(route.Output);
                }
            }
        }

        if (clear is not null)
        {
            foreach (IControllerOutput output in clear)
            {
                output.Clear();
            }
        }
    }

    /// <summary>Current client-side controller forwarding status.</summary>
    public BridgeClientControllerStatus Status
    {
        get
        {
            lock (_gate)
            {
                int routeCount = _routes.Count;
                return new(_active, routeCount, routeCount);
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await ClearRoutesAsync().ConfigureAwait(false);
    }

    // MARK: Lifecycle
    // ========================================================================

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (ControllerOutputKind() != ControllerOutput.Xbox360)
        {
            return;
        }

        try
        {
            SdlGamepadRuntime.EnsureInitialized();
            await RefreshRoutesAsync(stoppingToken).ConfigureAwait(false);

            while (!stoppingToken.IsCancellationRequested)
            {
                if (!SDL.WaitEventTimeout(out SDL.Event sdlEvent, (int)EventWaitTimeout.TotalMilliseconds))
                {
                    continue;
                }

                await ProcessEventAsync(sdlEvent, stoppingToken).ConfigureAwait(false);
                while (SDL.PollEvent(out SDL.Event queuedEvent))
                {
                    await ProcessEventAsync(queuedEvent, stoppingToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
        {
            LogControllerForwardingFailed(logger, exception.Message, null);
        }
        finally
        {
            await ClearRoutesAsync().ConfigureAwait(false);
        }
    }

    // MARK: SDL Events
    // ========================================================================

    private async Task ProcessEventAsync(SDL.Event sdlEvent, CancellationToken cancellationToken)
    {
        SDL.EventType type = (SDL.EventType)sdlEvent.Type;
        if (type is SDL.EventType.GamepadAdded or SDL.EventType.GamepadRemoved)
        {
            await RefreshRoutesAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        foreach (ControllerRoute route in SnapshotRoutes())
        {
            if (!route.Source.IsStateEvent(sdlEvent))
            {
                continue;
            }

            if (IsActive())
            {
                ControllerState state = route.Source.ReadState();
                route.Output.Send(in state);
            }

            break;
        }
    }

    private bool IsActive()
    {
        lock (_gate)
        {
            return _active;
        }
    }

    // MARK: Route Management
    // ========================================================================

    private async Task RefreshRoutesAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<SdlSteamControllerInfo> controllers = SdlSteamControllerCatalog.GetControllers();
        HashSet<ulong> seen = new(controllers.Count);

        foreach (SdlSteamControllerInfo controller in controllers)
        {
            _ = seen.Add(controller.SteamHandle);
            ControllerRoute? existing = FindRoute(controller.SteamHandle);
            if (existing is null)
            {
                await AddRouteAsync(controller, cancellationToken).ConfigureAwait(false);
            }
            else if (existing.Source.Controller.InstanceId != controller.InstanceId)
            {
                await ReplaceSourceAsync(controller, existing).ConfigureAwait(false);
            }
        }

        await RemoveMissingRoutesAsync(seen).ConfigureAwait(false);
    }

    private ControllerRoute? FindRoute(ulong steamHandle)
    {
        lock (_gate)
        {
            return _routes.GetValueOrDefault(steamHandle);
        }
    }

    private async Task AddRouteAsync(SdlSteamControllerInfo controller, CancellationToken cancellationToken)
    {
#pragma warning disable CA2000 // Ownership transfers to the route or is disposed in the catch block.
        SdlSteamControllerSource source = SdlSteamControllerSource.Open(controller);
#pragma warning restore CA2000
        IControllerOutput output;
        try
        {
            output = await outputFactory.ConnectXbox360Async(controller.Name, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await source.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        ControllerRoute route = new(controller.SteamHandle, source, output);
        route.Output.RumbleReceived += OnRumbleReceived;

        lock (_gate)
        {
            _routes[controller.SteamHandle] = route;
        }
    }

    private async Task ReplaceSourceAsync(SdlSteamControllerInfo controller, ControllerRoute route)
    {
#pragma warning disable CA2000 // Ownership transfers to the existing route.
        SdlSteamControllerSource source = SdlSteamControllerSource.Open(controller);
#pragma warning restore CA2000
        SdlSteamControllerSource oldSource;
        lock (_gate)
        {
            oldSource = route.Source;
            route.Source = source;
        }

        await oldSource.DisposeAsync().ConfigureAwait(false);
    }

    private async Task RemoveMissingRoutesAsync(HashSet<ulong> seen)
    {
        List<ControllerRoute> removed = [];
        lock (_gate)
        {
            foreach (KeyValuePair<ulong, ControllerRoute> route in _routes)
            {
                if (!seen.Contains(route.Key))
                {
                    removed.Add(route.Value);
                }
            }

            foreach (ControllerRoute route in removed)
            {
                _ = _routes.Remove(route.SteamHandle);
            }
        }

        foreach (ControllerRoute route in removed)
        {
            await DisposeRouteAsync(route).ConfigureAwait(false);
        }
    }

    private IReadOnlyList<ControllerRoute> SnapshotRoutes()
    {
        lock (_gate)
        {
            return [.. _routes.Values];
        }
    }

    private async ValueTask ClearRoutesAsync()
    {
        List<ControllerRoute> routes;
        lock (_gate)
        {
            routes = [.. _routes.Values];
            _routes.Clear();
        }

        foreach (ControllerRoute route in routes)
        {
            await DisposeRouteAsync(route).ConfigureAwait(false);
        }
    }

    private async ValueTask DisposeRouteAsync(ControllerRoute route)
    {
        route.Output.RumbleReceived -= OnRumbleReceived;
        route.Output.Clear();
        await route.Output.DisposeAsync().ConfigureAwait(false);
        await route.Source.DisposeAsync().ConfigureAwait(false);
    }

    // MARK: Feedback
    // ========================================================================

    private void OnRumbleReceived(object? sender, ControllerRumbleEventArgs args)
    {
        if (!IsActive())
        {
            return;
        }

        lock (_gate)
        {
            foreach (ControllerRoute route in _routes.Values)
            {
                if (ReferenceEquals(route.Output, sender))
                {
                    route.Source.SendRumble(args.Rumble);
                    return;
                }
            }
        }
    }

    // MARK: Settings
    // ========================================================================

    private ControllerOutput? ControllerOutputKind()
    {
        return settings.Current.Games.TryGetValue(options.ProfileId, out GameProfile? profile)
            ? profile.ControllerOutput
            : null;
    }

    // MARK: Logging
    // ========================================================================

    private static readonly Action<ILogger, string, Exception?> LogControllerForwardingFailed =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(1, nameof(LogControllerForwardingFailed)),
            "Controller forwarding failed: {Message}");

    private sealed class ControllerRoute(
        ulong steamHandle,
        SdlSteamControllerSource source,
        IControllerOutput output)
    {
        public ulong SteamHandle { get; } = steamHandle;

        public SdlSteamControllerSource Source { get; set; } = source;

        public IControllerOutput Output { get; } = output;
    }
}
