using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VirtualMouse.Forwarding;
using VirtualMouse.Runtime;
using VirtualMouse.Settings;
using VirtualMouse.Steam;

namespace VirtualMouse.Hosting;

internal sealed class ServerActiveClientLoop(
    ActiveClientRegistry clients,
    Func<int> getForegroundProcessId,
    TimeSpan pollInterval,
    Action<ActiveClientChangedEventArgs> activeClientChanged)
{
    public static ServerActiveClientLoop CreateDefault(
        ActiveClientRegistry clients,
        HostingSettings settings,
        ILogger logger,
        ControllerBroker? forwarding = null,
        MouseBroker? mouseForwarding = null)
    {
        return new ServerActiveClientLoop(
            clients,
            GetForegroundProcessId,
            TimeSpan.FromMilliseconds(settings.ForegroundPollMilliseconds),
            args => ActiveClientChanged(clients, logger, new SteamInputClient(), forwarding, mouseForwarding, args));
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        clients.ActiveClientChanged += OnActiveClientChanged;
        try
        {
            int lastForegroundProcessId = -1;
            while (!cancellationToken.IsCancellationRequested)
            {
                int foregroundProcessId = getForegroundProcessId();
                if (foregroundProcessId != lastForegroundProcessId)
                {
                    clients.RefreshClients(foregroundProcessId);
                    lastForegroundProcessId = foregroundProcessId;
                }

                await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            clients.ActiveClientChanged -= OnActiveClientChanged;
        }
    }

    private void OnActiveClientChanged(object? sender, ActiveClientChangedEventArgs args)
    {
        activeClientChanged(args);
    }

    private static int GetForegroundProcessId()
    {
        IntPtr window = GetForegroundWindow();
        if (window == IntPtr.Zero)
        {
            return 0;
        }

        _ = GetWindowThreadProcessId(window, out uint processId);
        return processId <= int.MaxValue ? (int)processId : 0;
    }

    private static void ActiveClientChanged(
        ActiveClientRegistry clients,
        ILogger logger,
        SteamInputClient steam,
        ControllerBroker? forwarding,
        MouseBroker? mouseForwarding,
        ActiveClientChangedEventArgs args)
    {
        HostingLog.ActiveClientChanged(logger, args.PreviousClientId, args.CurrentClientId);

        forwarding?.SetActiveClient(args.CurrentClientId);
        mouseForwarding?.SetActiveClient(args.CurrentClientId);

        try
        {
            uint? appId = FindSteamAppId(clients.GetStatus(), args.CurrentClientId);
            HostingLog.ClearingForcedSteamInputAppId(logger);
            steam.ForceConfigAsync(null).AsTask().GetAwaiter().GetResult();

            if (appId.HasValue)
            {
                HostingLog.ForcingSteamInputAppId(logger, appId.Value, args.CurrentClientId);
                steam.ForceConfigAsync(appId.Value).AsTask().GetAwaiter().GetResult();
            }
            else
            {
                HostingLog.NoSteamInputAppIdToForce(logger);
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
                ArgumentException or
                System.ComponentModel.Win32Exception)
        {
            HostingLog.SteamInputForcingFailed(logger, args.CurrentClientId, exception.Message);
        }
    }

    private static uint? FindSteamAppId(
        ActiveClientRegistryStatus status,
        Guid? clientId)
    {
        if (!clientId.HasValue)
        {
            return null;
        }

        foreach (ClientStatus client in status.Clients)
        {
            if (client.ClientId == clientId)
            {
                return client.SteamAppId;
            }
        }

        return null;
    }

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
