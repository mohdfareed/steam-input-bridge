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
    Func<Guid, IReadOnlyList<string>>? getDevices,
    Func<IReadOnlyList<string>, IReadOnlyList<string>>? formatDevices,
    ControllerBroker? forwarding)
{
    private readonly Lock _statusGate = new();
    private readonly Lock _refreshGate = new();
    private ServerHidHideStatus _status = new(false, false, false, [], [], [], null, null);

    public ServerHidHideStatus GetStatus()
    {
        lock (_statusGate)
        {
            return _status;
        }
    }

    public void Refresh(Guid? clientId)
    {
        lock (_refreshGate)
        {
            if (logger is null || hidHide is null)
            {
                return;
            }

            try
            {
                if (!TryCreateScope(out HidHideScope scope))
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
    }

    public void Clear()
    {
        lock (_refreshGate)
        {
            hidHide?.Clear();
            SetStatus(new ServerHidHideStatus(false, false, false, [], [], [], null, null));
        }
    }

    private bool TryCreateScope(out HidHideScope scope)
    {
        scope = HidHideScope.Create([]);
        if (profiles is null ||
            forwarding?.GetStatus().ControllerOutputEnabled == false)
        {
            return false;
        }

        ActiveClientRegistryStatus status = clients.GetStatus();
        HashSet<string> devices = new(StringComparer.OrdinalIgnoreCase);
        foreach (ClientStatus client in status.Clients)
        {
            GameProfile? profile = profiles.GetProfile(client.ProfileId);
            bool needsControllerOutput =
                profile?.ControllerOutput.GetValueOrDefault(ProfileControllerOutput.None) != ProfileControllerOutput.None;

            // HidHide normal mode does not need receiver executable paths.
            // The physical controller is hidden for the lifetime of the
            // profile client, not only while its receiver is foregrounded.
            // Client end/disconnect clears the runtime client and therefore
            // clears this scope.
            if (!needsControllerOutput)
            {
                continue;
            }

            foreach (string device in getDevices?.Invoke(client.ClientId) ?? [])
            {
                _ = devices.Add(device);
            }
        }

        scope = HidHideScope.Create(devices);
        return !scope.IsEmpty;
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
            IReadOnlyList<string> deviceLabels =
                formatDevices is null || status.HiddenDevices.Count == 0
                    ? []
                    : formatDevices(status.HiddenDevices);

            return new ServerHidHideStatus(
                status.ScopeActive,
                status.CloakEnabled,
                status.InverseEnabled,
                status.HiddenDevices,
                deviceLabels,
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
        lock (_statusGate)
        {
            _status = status;
        }
    }
}
