using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Forwarding.Controller.Routing;
using SteamInputBridge.Forwarding.Mouse;
using SteamInputBridge.HidHide;
using SteamInputBridge.Runtime;
using SteamInputBridge.Settings.Profiles;
using SteamInputBridge.Steam;

namespace SteamInputBridge.Hosting.Server.Orchestration.Active;

internal sealed class ServerActiveClientLoop(
    ActiveClientRegistry clients,
    Func<int> getForegroundProcessId,
    TimeSpan pollInterval,
    Action<ActiveClientChangedEventArgs>? activeClientChanged,
    ILogger? logger = null,
    SteamInputClient? steam = null,
    ProfilesService? profiles = null,
    HidHideService? hidHide = null,
    Func<ActiveClientRegistryStatus, Guid, IReadOnlyList<string>>? getHidHideDevices = null,
    Func<IReadOnlyList<string>, IReadOnlyList<string>>? formatHidHideDevices = null,
    ControllerBroker? forwarding = null,
    MouseBroker? mouseForwarding = null,
    TimeSpan? forwardingInactiveGrace = null)
{
    private readonly ServerSteamInputCoordinator _steamInput = new(clients, logger, steam);
    private readonly ServerHidHideCoordinator _hidHide = new(
        clients,
        logger,
        profiles,
        hidHide,
        getHidHideDevices,
        formatHidHideDevices,
        forwarding);
    private readonly Lock _forwardingGate = new();
    private readonly TimeSpan _forwardingInactiveGrace = forwardingInactiveGrace ?? TimeSpan.FromMilliseconds(500);
    private CancellationTokenSource? _forwardingInactiveDelay;
    private Guid? _forwardingActiveClientId;

    private static readonly TimeSpan ForegroundPollDelay = TimeSpan.FromMilliseconds(100);

    public static ServerActiveClientLoop CreateDefault(
        ActiveClientRegistry clients,
        ILogger logger,
        ProfilesService? profiles = null,
        HidHideService? hidHide = null,
        Func<ActiveClientRegistryStatus, Guid, IReadOnlyList<string>>? getHidHideDevices = null,
        Func<IReadOnlyList<string>, IReadOnlyList<string>>? formatHidHideDevices = null,
        ControllerBroker? forwarding = null,
        MouseBroker? mouseForwarding = null)
    {
        return new ServerActiveClientLoop(
            clients,
            ServerForegroundProcess.GetId,
            ForegroundPollDelay,
            activeClientChanged: null,
            logger,
            new SteamInputClient(),
            profiles,
            hidHide,
            getHidHideDevices,
            formatHidHideDevices,
            forwarding,
            mouseForwarding);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        clients.ActiveClientChanged += OnActiveClientChanged;
        try
        {
            _hidHide.Refresh(null);
            int lastForegroundProcessId = -1;
            while (!cancellationToken.IsCancellationRequested)
            {
                int foregroundProcessId = getForegroundProcessId();
                if (foregroundProcessId != lastForegroundProcessId)
                {
                    clients.RefreshClients(foregroundProcessId);
                    _hidHide.Refresh(clients.GetStatus().ActiveClientId);
                    lastForegroundProcessId = foregroundProcessId;
                }

                await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            clients.ActiveClientChanged -= OnActiveClientChanged;
            _hidHide.Clear();
            ClearForwardingImmediately();
        }
    }

    public ServerSteamInputStatus GetSteamInputStatus()
    {
        return _steamInput.GetStatus();
    }

    public ServerHidHideStatus GetHidHideStatus()
    {
        return _hidHide.GetStatus();
    }

    public void RefreshHidHide()
    {
        _hidHide.Refresh(clients.GetStatus().ActiveClientId);
    }

    public void ClearHidHide()
    {
        _hidHide.Clear();
    }

    private void OnActiveClientChanged(object? sender, ActiveClientChangedEventArgs args)
    {
        if (activeClientChanged is not null)
        {
            activeClientChanged(args);
            return;
        }

        ActiveClientChanged(args);
    }

    private void ActiveClientChanged(ActiveClientChangedEventArgs args)
    {
        if (logger is null)
        {
            return;
        }

        HostingLog.ActiveClientChanged(logger, args.PreviousClientId, args.CurrentClientId);

        ApplyForwardingActiveClient(args.CurrentClientId);

        _steamInput.Apply(args.CurrentClientId);
        _hidHide.Refresh(args.CurrentClientId);
    }

    private void ApplyForwardingActiveClient(Guid? clientId)
    {
        CancellationTokenSource? previousDelay;
        bool applyNow = false;

        lock (_forwardingGate)
        {
            previousDelay = _forwardingInactiveDelay;
            _forwardingInactiveDelay = null;

            if (clientId.HasValue || _forwardingActiveClientId is null || _forwardingInactiveGrace <= TimeSpan.Zero)
            {
                _forwardingActiveClientId = clientId;
                applyNow = true;
            }
            else
            {
                CancellationTokenSource delay = new();
                _forwardingInactiveDelay = delay;
                _ = ClearForwardingAfterDelayAsync(delay);
            }
        }

        previousDelay?.Cancel();
        if (applyNow)
        {
            SetForwardingActiveClient(clientId);
        }
    }

    private async Task ClearForwardingAfterDelayAsync(CancellationTokenSource delay)
    {
        try
        {
            await Task.Delay(_forwardingInactiveGrace, delay.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        bool apply = false;
        lock (_forwardingGate)
        {
            if (!ReferenceEquals(_forwardingInactiveDelay, delay))
            {
                return;
            }

            _forwardingInactiveDelay = null;
            if (_forwardingActiveClientId.HasValue)
            {
                _forwardingActiveClientId = null;
                apply = true;
            }
        }

        if (apply)
        {
            SetForwardingActiveClient(null);
        }
    }

    private void ClearForwardingImmediately()
    {
        CancellationTokenSource? delay;
        lock (_forwardingGate)
        {
            delay = _forwardingInactiveDelay;
            _forwardingInactiveDelay = null;
            _forwardingActiveClientId = null;
        }

        delay?.Cancel();
        SetForwardingActiveClient(null);
    }

    private void SetForwardingActiveClient(Guid? clientId)
    {
        forwarding?.SetActiveClient(clientId);
        mouseForwarding?.SetActiveClient(clientId);
    }
}
