using System;
using System.Threading;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Runtime;
using SteamInputBridge.Steam;

namespace SteamInputBridge.Hosting.Server.Orchestration.Active;

internal sealed class ServerSteamInputCoordinator(
    ActiveClientRegistry clients,
    ILogger? logger,
    SteamInputClient? steam)
{
    private readonly Lock _gate = new();
    private ServerSteamInputStatus _status = new(false, null, null, null);

    public ServerSteamInputStatus GetStatus()
    {
        lock (_gate)
        {
            return _status;
        }
    }

    public void Apply(Guid? clientId)
    {
        if (logger is null || steam is null)
        {
            return;
        }

        try
        {
            uint? appId = FindSteamAppId(clients.GetStatus(), clientId);
            HostingLog.ClearingForcedSteamInputAppId(logger);
            steam.ForceConfigAsync(null).AsTask().GetAwaiter().GetResult();

            if (appId.HasValue)
            {
                HostingLog.ForcingSteamInputAppId(logger, appId.Value, clientId);
                steam.ForceConfigAsync(appId.Value).AsTask().GetAwaiter().GetResult();
                SetStatus(new ServerSteamInputStatus(true, appId.Value, clientId, null));
            }
            else
            {
                HostingLog.NoSteamInputAppIdToForce(logger);
                SetStatus(new ServerSteamInputStatus(false, null, clientId, null));
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
                ArgumentException or
                System.ComponentModel.Win32Exception)
        {
            HostingLog.SteamInputForcingFailed(logger, clientId, exception.Message);
            SetStatus(new ServerSteamInputStatus(false, null, clientId, exception.Message));
        }
    }

    private void SetStatus(ServerSteamInputStatus status)
    {
        lock (_gate)
        {
            _status = status;
        }
    }

    private static uint? FindSteamAppId(ActiveClientRegistryStatus status, Guid? clientId)
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
}
