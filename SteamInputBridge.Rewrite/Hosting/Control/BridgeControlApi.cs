using System.Threading.Tasks;
using PolyType;
using StreamJsonRpc;

namespace SteamInputBridge.Hosting.Control;

/// <summary>General client/server control API.</summary>
[JsonRpcContract]
[GenerateShape(IncludeMethods = MethodShapeFlags.PublicInstance)]
internal partial interface IBridgeControlApi
{
    /// <summary>Registers a client process with the server.</summary>
    Task ConnectAsync(int processId, string profileId);
}
