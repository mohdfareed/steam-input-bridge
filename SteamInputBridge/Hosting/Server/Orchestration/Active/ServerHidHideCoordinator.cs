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
    private readonly Dictionary<Guid, HashSet<string>> _retainedClientDevices = [];
    private HidHideScope? _lastScope;
    private ServerHidHideStatus _status = new(false, false, false, [], [], [], null, null);

    public ServerHidHideStatus GetStatus()
    {
        ServerHidHideStatus cached;
        lock (_statusGate)
        {
            cached = _status;
        }

        if (hidHide is null)
        {
            return cached;
        }

        try
        {
            return CreateLiveStatus(hidHide.GetStatus(), cached.ClientId, cached.LastError);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            ArgumentException or
            System.ComponentModel.Win32Exception or
            System.IO.IOException or
            UnauthorizedAccessException)
        {
            return cached with { LastError = exception.Message };
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
                    ApplyNoScope(clientId);
                    return;
                }

                if (_lastScope?.HasSameValues(scope) == true)
                {
                    SetStatus(CreateCachedStatus(scope, clientId, null));
                    return;
                }

                hidHide.Apply(scope);
                _lastScope = scope;
                SetStatus(CreateCachedStatus(scope, clientId, null));
            }
            catch (Exception exception) when (
                exception is InvalidOperationException or
                ArgumentException or
                System.ComponentModel.Win32Exception or
                System.IO.IOException or
                UnauthorizedAccessException)
            {
                HostingLog.HidHideUpdateFailed(logger, clientId, exception.Message);
                SetStatus(_lastScope is { } scope
                    ? CreateCachedStatus(scope, clientId, exception.Message)
                    : new ServerHidHideStatus(
                        false,
                        false,
                        false,
                        [],
                        [],
                        GetRegisteredApplications(),
                        clientId,
                        exception.Message));
            }
        }
    }

    public void Clear()
    {
        lock (_refreshGate)
        {
            hidHide?.Clear();
            _retainedClientDevices.Clear();
            _lastScope = null;
            SetStatus(new ServerHidHideStatus(false, false, false, [], [], [], null, null));
        }
    }

    private bool TryCreateScope(out HidHideScope scope)
    {
        scope = HidHideScope.Create([]);
        if (profiles is null ||
            forwarding?.GetStatus().ControllerOutputEnabled == false)
        {
            _retainedClientDevices.Clear();
            return false;
        }

        ActiveClientRegistryStatus status = clients.GetStatus();
        HashSet<Guid> liveClients = [];
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

            _ = liveClients.Add(client.ClientId);
            if (!_retainedClientDevices.TryGetValue(client.ClientId, out HashSet<string>? retained))
            {
                retained = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _retainedClientDevices[client.ClientId] = retained;
            }

            // HidHide can change what Steam exposes to the client. Once a
            // running client has resolved a physical controller path, keep
            // that path hidden for the client lifetime instead of shrinking
            // the scope from transient client-side SDL scans.
            foreach (string device in getDevices?.Invoke(client.ClientId) ?? [])
            {
                _ = retained.Add(device);
            }

            foreach (string device in retained)
            {
                _ = devices.Add(device);
            }
        }

        List<Guid> retainedClientIds = [.. _retainedClientDevices.Keys];
        foreach (Guid clientId in retainedClientIds)
        {
            if (!liveClients.Contains(clientId))
            {
                _ = _retainedClientDevices.Remove(clientId);
            }
        }

        scope = HidHideScope.Create(devices);
        return !scope.IsEmpty;
    }

    private void ApplyNoScope(Guid? clientId)
    {
        _retainedClientDevices.Clear();
        if (_lastScope is not null)
        {
            hidHide?.Clear();
            _lastScope = null;
        }

        SetStatus(new ServerHidHideStatus(false, false, false, [], [], GetRegisteredApplications(), clientId, null));
    }

    private void SetStatus(ServerHidHideStatus status)
    {
        lock (_statusGate)
        {
            _status = status;
        }
    }

    private ServerHidHideStatus CreateCachedStatus(HidHideScope scope, Guid? clientId, string? error)
    {
        IReadOnlyList<string> devices = scope.DeviceInstancePaths;
        IReadOnlyList<string> deviceLabels =
            formatDevices is null || devices.Count == 0
                ? []
                : formatDevices(devices);

        return new ServerHidHideStatus(
            !scope.IsEmpty,
            CloakEnabled: !scope.IsEmpty,
            InverseEnabled: false,
            devices,
            deviceLabels,
            GetRegisteredApplications(),
            clientId,
            error);
    }

    private ServerHidHideStatus CreateLiveStatus(
        HidHideFirewallStatus status,
        Guid? clientId,
        string? error)
    {
        IReadOnlyList<string> devices = status.HiddenDevices;
        IReadOnlyList<string> deviceLabels =
            formatDevices is null || devices.Count == 0
                ? []
                : formatDevices(devices);

        return new ServerHidHideStatus(
            status.ScopeActive,
            status.CloakEnabled,
            status.InverseEnabled,
            devices,
            deviceLabels,
            status.RegisteredApplications,
            clientId,
            error);
    }

    private IReadOnlyList<string> GetRegisteredApplications()
    {
        lock (_statusGate)
        {
            return _status.RegisteredApplications;
        }
    }
}
