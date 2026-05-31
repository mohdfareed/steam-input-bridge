using System;
using System.Collections.Generic;
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

    /// <summary>Asks a connected client to exit.</summary>
    Task StopClientAsync(Guid connectionId);
}

/// <summary>General server/client control API.</summary>
[JsonRpcContract]
[GenerateShape(IncludeMethods = MethodShapeFlags.PublicInstance)]
public partial interface IBridgeClientApi
{
    /// <summary>Asks the client to exit.</summary>
    Task StopAsync();
}

/// <summary>Current server status snapshot.</summary>
/// <param name="clients">Connected client snapshots.</param>
public sealed class BridgeServerStatus(IReadOnlyList<BridgeClientStatus> clients)
{
    /// <summary>Connected client snapshots.</summary>
    public IReadOnlyList<BridgeClientStatus> Clients { get; } = clients;

    /// <summary>Number of connected clients.</summary>
    public int ClientsCount => Clients.Count;
}

/// <summary>Connected client status snapshot.</summary>
/// <param name="connectionId">Control connection id.</param>
/// <param name="processId">Client process id.</param>
/// <param name="profileId">Client profile id.</param>
public sealed class BridgeClientStatus(Guid connectionId, int processId, string profileId)
{
    /// <summary>Control connection id.</summary>
    public Guid ConnectionId { get; } = connectionId;

    /// <summary>Client process id.</summary>
    public int ProcessId { get; } = processId;

    /// <summary>Client profile id.</summary>
    public string ProfileId { get; } = profileId;
}
