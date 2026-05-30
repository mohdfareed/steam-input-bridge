using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Forwarding.Controller.Routing;
using SteamInputBridge.Forwarding.Mouse;
using SteamInputBridge.Runtime;
using SteamInputBridge.Steam;

namespace SteamInputBridge.Hosting.Server.Orchestration.Active;

internal sealed class ServerActiveClientLoop(
    ActiveClientRegistry clients,
    Func<int> getForegroundProcessId,
    TimeSpan pollInterval,
    Action<ActiveClientChangedEventArgs>? activeClientChanged,
    ILogger? logger = null,
    SteamInputClient? steam = null,
    ControllerBroker? forwarding = null,
    MouseBroker? mouseForwarding = null,
    Action? stateChanged = null)
{
    private readonly ServerSteamInputCoordinator _steamInput = new(clients, logger, steam);

    private static readonly TimeSpan ForegroundPollDelay = TimeSpan.FromMilliseconds(100);

    public static ServerActiveClientLoop CreateDefault(
        ActiveClientRegistry clients,
        ILogger logger,
        ControllerBroker? forwarding = null,
        MouseBroker? mouseForwarding = null,
        Action? stateChanged = null)
    {
        return new ServerActiveClientLoop(
            clients,
            ServerForegroundProcess.GetId,
            ForegroundPollDelay,
            activeClientChanged: null,
            logger,
            new SteamInputClient(),
            forwarding,
            mouseForwarding,
            stateChanged: stateChanged);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        clients.ActiveClientChanged += OnActiveClientChanged;
        try
        {
            int lastForegroundProcessId = -1;
            // This is the only foreground polling loop in the server. It
            // centralizes foreground-to-active-client selection so Steam
            // forcing and forwarding gates do not grow their own timers.
            while (!cancellationToken.IsCancellationRequested)
            {
                int foregroundProcessId = getForegroundProcessId();
                if (foregroundProcessId != lastForegroundProcessId)
                {
                    clients.RefreshClients(foregroundProcessId);
                    stateChanged?.Invoke();
                    lastForegroundProcessId = foregroundProcessId;
                }

                await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            clients.ActiveClientChanged -= OnActiveClientChanged;
            SetForwardingActiveClient(null);
        }
    }

    public ServerSteamInputStatus GetSteamInputStatus()
    {
        return _steamInput.GetStatus();
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
        if (logger is not null)
        {
            HostingLog.ActiveClientChanged(logger, args.PreviousClientId, args.CurrentClientId);
        }

        // Steam config and virtual output follow the exact foreground receiver.
        // Do not keep forwarding alive after focus leaves; Windows and shells
        // can react to tiny controller stick drift even when a game would not.
        _steamInput.Apply(args.CurrentClientId);
        SetForwardingActiveClient(args.CurrentClientId);
        stateChanged?.Invoke();
    }

    private void SetForwardingActiveClient(Guid? clientId)
    {
        forwarding?.SetActiveClient(clientId);
        mouseForwarding?.SetActiveClient(clientId);
    }
}
