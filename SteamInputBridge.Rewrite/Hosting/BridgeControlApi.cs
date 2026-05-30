using System.Threading.Tasks;
using PolyType;
using StreamJsonRpc;

namespace SteamInputBridge.Hosting;

/// <summary>General client/server control API.</summary>
[JsonRpcContract]
[GenerateShape(IncludeMethods = MethodShapeFlags.PublicInstance)]
public partial interface IBridgeControlApi
{
    /// <summary>Control pipe name.</summary>
    const string Name = "SteamInputBridge";

    /// <summary>Registers a client process with the server.</summary>
    Task ConnectAsync(int processId, string profileId);

    /// <summary>Gets current server status.</summary>
    Task<BridgeServerStatus> GetStatusAsync();
}

/// <summary>Current server status.</summary>
/// <param name="connectedClientCount">Number of connected clients.</param>
public sealed class BridgeServerStatus(int connectedClientCount)
{
    /// <summary>Number of connected clients.</summary>
    public int ConnectedClientCount { get; } = connectedClientCount;
}
