using System;
using System.Threading.Tasks;

namespace SteamInputBridge.Hosting.Server;

internal sealed class BridgeControlSession(BridgeService service, Guid connectionId) : IBridgeControlApi
{
    /// <inheritdoc />
    public Task ConnectAsync(int processId, string profileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        _ = service.RegisterClient(connectionId, processId, profileId);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<BridgeServerStatus> GetStatusAsync()
    {
        return Task.FromResult(service.Status);
    }
}
