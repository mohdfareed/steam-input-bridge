using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Diagnostics;

namespace SteamInputBridge.Hosting.Server;

internal sealed class BridgeControlSession(BridgeService service, Guid connectionId, IBridgeClientApi client, ILogger logger)
    : IBridgeControlApi
{
    /// <inheritdoc />
    public async Task ConnectAsync(int processId, string profileId, uint? steamAppId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        _ = await service.RegisterClientAsync(connectionId, processId, profileId, steamAppId, client)
            .ConfigureAwait(false);

        BridgeLog.ClientRegistered(logger, processId, profileId);
    }

    /// <inheritdoc />
    public Task<BridgeServerStatus> GetStatusAsync()
    {
        return Task.FromResult(service.Status);
    }

    /// <inheritdoc />
    public Task StopClientAsync(Guid connectionId)
    {
        return service.StopClientAsync(connectionId);
    }
}
