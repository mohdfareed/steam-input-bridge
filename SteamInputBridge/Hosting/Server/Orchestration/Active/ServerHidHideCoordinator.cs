using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Forwarding.Controller.Routing;
using SteamInputBridge.HidHide;
using SteamInputBridge.Runtime;
using SteamInputBridge.Settings.Profiles;
using ProfileControllerOutput = SteamInputBridge.Settings.Profiles.ControllerOutput;

namespace SteamInputBridge.Hosting.Server.Orchestration.Active;

internal sealed class ServerHidHideCoordinator(
    ActiveClientRegistry clients,
    ILogger? logger,
    ProfilesService? profiles,
    HidHideService? hidHide,
    Func<ActiveClientRegistryStatus, Guid, IReadOnlyList<string>>? getDevices,
    Func<IReadOnlyList<string>, IReadOnlyList<string>>? formatDevices,
    ControllerBroker? forwarding)
{
    private readonly Lock _gate = new();
    private ServerHidHideStatus _status = new(false, false, false, [], [], [], null, null);

    public ServerHidHideStatus GetStatus()
    {
        lock (_gate)
        {
            return _status;
        }
    }

    public void Refresh(Guid? clientId)
    {
        if (logger is null || hidHide is null)
        {
            return;
        }

        try
        {
            if (!TryCreateScope(clientId, out HidHideScope scope))
            {
                hidHide.Clear();
                SetStatus(ReadStatus(clientId, null));
                return;
            }

            hidHide.Apply(scope);
            SetStatus(ReadStatus(clientId, null));
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
                ArgumentException or
                System.ComponentModel.Win32Exception or
                System.IO.IOException or
                UnauthorizedAccessException)
        {
            HostingLog.HidHideUpdateFailed(logger, clientId, exception.Message);
            SetStatus(ReadStatus(clientId, exception.Message));
        }
    }

    public void Clear()
    {
        hidHide?.Clear();
    }

    private bool TryCreateScope(Guid? clientId, out HidHideScope scope)
    {
        scope = HidHideScope.Create([], []);
        if (!clientId.HasValue || profiles is null)
        {
            return false;
        }

        ActiveClientRegistryStatus status = clients.GetStatus();
        ClientStatus? client = FindClient(status, clientId.Value);
        if (client is null)
        {
            return false;
        }

        GameProfile? profile = profiles.GetProfile(client.ProfileId);
        if (profile is null ||
            profile.ControllerOutput.GetValueOrDefault(ProfileControllerOutput.None) == ProfileControllerOutput.None ||
            forwarding?.GetStatus().ControllerOutputEnabled == false)
        {
            return false;
        }

        scope = HidHideScope.Create(
            getDevices?.Invoke(status, clientId.Value) ?? [],
            GetExecutablePaths(client.OwnedProcesses));
        return true;
    }

    private ServerHidHideStatus ReadStatus(Guid? clientId, string? error)
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

    private void SetStatus(ServerHidHideStatus status)
    {
        lock (_gate)
        {
            _status = status;
        }
    }

    private IReadOnlyList<string> GetDeviceLabels(IReadOnlyList<string> devicePaths)
    {
        return formatDevices is null || devicePaths.Count == 0
            ? []
            : formatDevices(devicePaths);
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
}
