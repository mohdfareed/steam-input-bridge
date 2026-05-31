using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Diagnostics;

namespace SteamInputBridge.Hosting.Server;

internal sealed class BridgeControlSession(BridgeService service, Guid connectionId, IBridgeClientApi client, ILogger logger)
    : IBridgeControlApi
{
    /// <inheritdoc />
    public Task ConnectAsync(int processId, string profileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        _ = service.RegisterClient(connectionId, processId, profileId, client);

        BridgeLog.ClientRegistered(logger, processId, profileId);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<BridgeServerStatus> GetStatusAsync()
    {
        return Task.FromResult(service.Status);
    }
}
