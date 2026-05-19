using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VirtualMouse.Forwarding;
using VirtualMouse.Runtime;
using VirtualMouse.Settings;
using VirtualMouse.Steam;

namespace VirtualMouse.Hosting;

internal sealed class ActiveClientOrchestration(
    ActiveClientRegistry runtime,
    Func<int> getForegroundProcessId,
    TimeSpan pollInterval,
    Action<ActiveClientChangedEventArgs> activeClientChanged)
{
    public static ActiveClientOrchestration CreateDefault(
        ActiveClientRegistry runtime,
        HostingSettings settings,
        ILogger logger,
        ControllerBroker? forwarding = null,
        MouseBroker? mouseForwarding = null)
    {
        return new ActiveClientOrchestration(
            runtime,
            GetForegroundProcessId,
            TimeSpan.FromMilliseconds(settings.ForegroundPollMilliseconds),
            args => ActiveClientChanged(runtime, logger, new SteamInputClient(), forwarding, mouseForwarding, args));
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        runtime.ActiveClientChanged += OnActiveClientChanged;
        try
        {
            int lastForegroundProcessId = -1;
            while (!cancellationToken.IsCancellationRequested)
            {
                int foregroundProcessId = getForegroundProcessId();
                if (foregroundProcessId != lastForegroundProcessId)
                {
                    runtime.RefreshActiveClient(foregroundProcessId);
                    lastForegroundProcessId = foregroundProcessId;
                }

                await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            runtime.ActiveClientChanged -= OnActiveClientChanged;
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
        ActiveClientRegistry runtime,
        ILogger logger,
        SteamInputClient steam,
        ControllerBroker? forwarding,
        MouseBroker? mouseForwarding,
        ActiveClientChangedEventArgs args)
    {
        logger.LogInformation(
            "Active client changed: previous={PreviousClientId} current={CurrentClientId}",
            args.PreviousClientId?.ToString() ?? "none",
            args.CurrentClientId?.ToString() ?? "none");

        forwarding?.SetActiveClient(args.CurrentClientId);
        mouseForwarding?.SetActiveClient(args.CurrentClientId);

        try
        {
            uint? appId = FindSteamAppId(runtime.GetStatus(), args.CurrentClientId);
            logger.LogInformation("Clearing forced Steam Input app id.");
            steam.ForceConfigAsync(null).AsTask().GetAwaiter().GetResult();

            if (appId.HasValue)
            {
                logger.LogInformation(
                    "Forcing Steam Input app id {AppId} for client {ClientId}.",
                    appId.Value.ToString(CultureInfo.InvariantCulture),
                    args.CurrentClientId?.ToString() ?? "none");
                steam.ForceConfigAsync(appId.Value).AsTask().GetAwaiter().GetResult();
            }
            else
            {
                logger.LogDebug("No Steam Input app id to force.");
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
                ArgumentException or
                System.ComponentModel.Win32Exception)
        {
            logger.LogWarning(
                "Steam Input forcing failed for client {ClientId}: {Message}",
                args.CurrentClientId?.ToString() ?? "none",
                exception.Message);
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
