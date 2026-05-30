using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Diagnostics;
using SteamInputBridge.Hosting.Control;

namespace SteamInputBridge.Hosting.Server;

/// <summary>Service implementation of the general control API.</summary>
public sealed class BridgeControlService(ILogger<BridgeControlService> logger) : IBridgeControlApi
{
    /// <inheritdoc />
    public Task ConnectAsync(int processId, string profileId)
    {
        BridgeLog.ClientConnectAttempted(logger, processId, profileId);
        return Task.CompletedTask;
    }
}
