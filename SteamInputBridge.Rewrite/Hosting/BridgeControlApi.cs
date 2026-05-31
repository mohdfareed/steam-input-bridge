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
    Task ConnectAsync(int processId, string profileId, uint? steamAppId);

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
/// <param name="shortcuts">Configured shortcut snapshots.</param>
public sealed class BridgeServerStatus(IReadOnlyList<BridgeClientStatus> clients, IReadOnlyList<BridgeShortcutStatus> shortcuts)
{
    /// <summary>Connected client snapshots.</summary>
    public IReadOnlyList<BridgeClientStatus> Clients { get; } = clients;

    /// <summary>Configured shortcut snapshots.</summary>
    public IReadOnlyList<BridgeShortcutStatus> Shortcuts { get; } = shortcuts;

    /// <summary>Number of connected clients.</summary>
    public int ClientsCount => Clients.Count;
}

/// <summary>Connected client status snapshot.</summary>
/// <param name="connectionId">Control connection id.</param>
/// <param name="processId">Client process id.</param>
/// <param name="profileId">Client profile id.</param>
/// <param name="steamAppId">Steam app id reported by the client.</param>
public sealed class BridgeClientStatus(Guid connectionId, int processId, string profileId, uint? steamAppId)
{
    /// <summary>Control connection id.</summary>
    public Guid ConnectionId { get; } = connectionId;

    /// <summary>Client process id.</summary>
    public int ProcessId { get; } = processId;

    /// <summary>Client profile id.</summary>
    public string ProfileId { get; } = profileId;

    /// <summary>Steam app id reported by the client.</summary>
    public uint? SteamAppId { get; } = steamAppId;
}

/// <summary>Configured shortcut status snapshot.</summary>
/// <param name="keys">Shortcut key combination.</param>
/// <param name="targets">Shortcut target names.</param>
/// <param name="action">Shortcut action.</param>
/// <param name="pressed">Whether the shortcut is currently pressed.</param>
public sealed class BridgeShortcutStatus(
    string keys,
    IReadOnlyList<string> targets,
    string action,
    bool pressed)
{
    /// <summary>Shortcut key combination.</summary>
    public string Keys { get; } = keys;

    /// <summary>Shortcut target names.</summary>
    public IReadOnlyList<string> Targets { get; } = targets;

    /// <summary>Shortcut action.</summary>
    public string Action { get; } = action;

    /// <summary>Whether the shortcut is currently pressed.</summary>
    public bool Pressed { get; } = pressed;
}
