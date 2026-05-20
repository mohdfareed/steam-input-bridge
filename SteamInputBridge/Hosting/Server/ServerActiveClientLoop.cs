using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Forwarding.Controller;
using SteamInputBridge.Forwarding.Mouse;
using SteamInputBridge.HidHide;
using SteamInputBridge.Runtime;
using SteamInputBridge.Settings.Profiles;
using SteamInputBridge.Steam;
using ProfileControllerOutput = SteamInputBridge.Settings.Profiles.ControllerOutput;

namespace SteamInputBridge.Hosting.Server;

internal sealed class ServerActiveClientLoop(
    ActiveClientRegistry clients,
    Func<int> getForegroundProcessId,
    TimeSpan pollInterval,
    Action<ActiveClientChangedEventArgs>? activeClientChanged,
    ILogger? logger = null,
    SteamInputClient? steam = null,
    ProfilesService? profiles = null,
    HidHideProfileFirewall? hidHide = null,
    Func<ActiveClientRegistryStatus, Guid, IReadOnlyList<string>>? getHidHideDevices = null,
    Func<IReadOnlyList<string>, IReadOnlyList<string>>? formatHidHideDevices = null,
    ControllerBroker? forwarding = null,
    MouseBroker? mouseForwarding = null)
{
    private readonly Lock _steamStatusGate = new();
    private readonly Lock _hidHideStatusGate = new();
    private ServerSteamInputStatus _steamStatus = new(false, null, null, null);
    private ServerHidHideStatus _hidHideStatus = new(false, false, false, [], [], [], null, null);

    private static readonly TimeSpan ForegroundPollDelay = TimeSpan.FromMilliseconds(100);

    public static ServerActiveClientLoop CreateDefault(
        ActiveClientRegistry clients,
        ILogger logger,
        ProfilesService? profiles = null,
        HidHideProfileFirewall? hidHide = null,
        Func<ActiveClientRegistryStatus, Guid, IReadOnlyList<string>>? getHidHideDevices = null,
        Func<IReadOnlyList<string>, IReadOnlyList<string>>? formatHidHideDevices = null,
        ControllerBroker? forwarding = null,
        MouseBroker? mouseForwarding = null)
    {
        return new ServerActiveClientLoop(
            clients,
            GetForegroundProcessId,
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
            UpdateHidHide(null);
            int lastForegroundProcessId = -1;
            while (!cancellationToken.IsCancellationRequested)
            {
                int foregroundProcessId = getForegroundProcessId();
                if (foregroundProcessId != lastForegroundProcessId)
                {
                    clients.RefreshClients(foregroundProcessId);
                    UpdateHidHide(clients.GetStatus().ActiveClientId);
                    lastForegroundProcessId = foregroundProcessId;
                }

                await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            clients.ActiveClientChanged -= OnActiveClientChanged;
            hidHide?.Clear();
        }
    }

    public ServerSteamInputStatus GetSteamInputStatus()
    {
        lock (_steamStatusGate)
        {
            return _steamStatus;
        }
    }

    public ServerHidHideStatus GetHidHideStatus()
    {
        lock (_hidHideStatusGate)
        {
            return _hidHideStatus;
        }
    }

    public void RefreshHidHide()
    {
        UpdateHidHide(clients.GetStatus().ActiveClientId);
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

    private void ActiveClientChanged(ActiveClientChangedEventArgs args)
    {
        if (logger is null || steam is null)
        {
            return;
        }

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
                SetSteamInputStatus(new ServerSteamInputStatus(true, appId.Value, args.CurrentClientId, null));
            }
            else
            {
                HostingLog.NoSteamInputAppIdToForce(logger);
                SetSteamInputStatus(new ServerSteamInputStatus(false, null, args.CurrentClientId, null));
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
                ArgumentException or
                System.ComponentModel.Win32Exception)
        {
            HostingLog.SteamInputForcingFailed(logger, args.CurrentClientId, exception.Message);
            SetSteamInputStatus(new ServerSteamInputStatus(false, null, args.CurrentClientId, exception.Message));
        }

        UpdateHidHide(args.CurrentClientId);
    }

    private void UpdateHidHide(Guid? clientId)
    {
        if (logger is null || hidHide is null)
        {
            return;
        }

        try
        {
            if (!clientId.HasValue || profiles is null)
            {
                hidHide.Clear();
                SetHidHideStatus(ReadHidHideStatus(clientId, null));
                return;
            }

            ActiveClientRegistryStatus status = clients.GetStatus();
            ClientStatus? client = FindClient(status, clientId.Value);
            if (client is null)
            {
                hidHide.Clear();
                SetHidHideStatus(ReadHidHideStatus(clientId, null));
                return;
            }

            GameProfile? profile = profiles.GetProfile(client.ProfileId);
            if (profile is null ||
                profile.ControllerOutput.GetValueOrDefault(ProfileControllerOutput.None) == ProfileControllerOutput.None ||
                forwarding?.GetStatus().ControllerOutputEnabled == false)
            {
                hidHide.Clear();
                SetHidHideStatus(ReadHidHideStatus(clientId, null));
                return;
            }

            IReadOnlyList<string> devices = getHidHideDevices?.Invoke(status, clientId.Value) ?? [];
            IReadOnlyList<string> applications = GetExecutablePaths(client.OwnedProcesses);
            HidHideScope scope = HidHideScope.Create(
                devices,
                applications);
            hidHide.Apply(scope);
            SetHidHideStatus(ReadHidHideStatus(clientId, null));
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
                ArgumentException or
                System.ComponentModel.Win32Exception or
                System.IO.IOException or
                UnauthorizedAccessException)
        {
            HostingLog.HidHideUpdateFailed(logger, clientId, exception.Message);
            SetHidHideStatus(ReadHidHideStatus(clientId, exception.Message));
        }
    }

    private void SetSteamInputStatus(ServerSteamInputStatus status)
    {
        lock (_steamStatusGate)
        {
            _steamStatus = status;
        }
    }

    private void SetHidHideStatus(ServerHidHideStatus status)
    {
        lock (_hidHideStatusGate)
        {
            _hidHideStatus = status;
        }
    }

    private ServerHidHideStatus ReadHidHideStatus(Guid? clientId, string? error)
    {
        if (hidHide is null)
        {
            return new ServerHidHideStatus(false, false, false, [], [], [], clientId, error);
        }

        try
        {
            HidHideFirewallStatus status = hidHide.GetStatus();
            return new ServerHidHideStatus(
                status.ScopeActive,
                status.CloakEnabled,
                status.InverseEnabled,
                status.HiddenDevices,
                GetDeviceLabels(status.HiddenDevices),
                status.RegisteredApplications,
                clientId,
                error);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
                ArgumentException or
                System.ComponentModel.Win32Exception or
                System.IO.IOException or
                UnauthorizedAccessException)
        {
            return new ServerHidHideStatus(false, false, false, [], [], [], clientId, error ?? exception.Message);
        }
    }

    private IReadOnlyList<string> GetDeviceLabels(IReadOnlyList<string> devicePaths)
    {
        return formatHidHideDevices is null || devicePaths.Count == 0
            ? []
            : formatHidHideDevices(devicePaths);
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

    private static ClientStatus? FindClient(ActiveClientRegistryStatus status, Guid clientId)
    {
        foreach (ClientStatus client in status.Clients)
        {
            if (client.ClientId == clientId)
            {
                return client;
            }
        }

        return null;
    }

    private static List<string> GetExecutablePaths(IReadOnlyList<ObservedGameProcess> processes)
    {
        List<string> paths = [];
        foreach (ObservedGameProcess process in processes)
        {
            if (GameProcessHost.GetExecutablePath(process.ProcessId) is { Length: > 0 } path)
            {
                paths.Add(path);
            }
        }

        return paths;
    }

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
