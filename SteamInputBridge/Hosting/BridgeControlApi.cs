using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PolyType;
using SteamInputBridge.Settings;
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

    /// <summary>Gets current client runtime status.</summary>
    Task<BridgeClientRuntimeStatus> GetStatusAsync();

    /// <summary>Sets whether this client is the active forwarding client.</summary>
    Task SetActiveAsync(bool active);
}

/// <summary>Current server status snapshot.</summary>
public sealed class BridgeServerStatus(
    IReadOnlyList<BridgeProfileStatus> profiles,
    IReadOnlyList<BridgeClientStatus> clients,
    IReadOnlyList<BridgeShortcutStatus> shortcuts,
    BridgeMouseStatus mouse,
    BridgeTeensyStatus teensy,
    BridgeControllerStatus controller,
    BridgeSteamInputStatus steamInput)
{
    /// <summary>Resolved profile snapshots.</summary>
    public IReadOnlyList<BridgeProfileStatus> Profiles { get; } = profiles;

    /// <summary>Connected client snapshots.</summary>
    public IReadOnlyList<BridgeClientStatus> Clients { get; } = clients;

    /// <summary>Configured shortcut snapshots.</summary>
    public IReadOnlyList<BridgeShortcutStatus> Shortcuts { get; } = shortcuts;

    /// <summary>Number of connected clients.</summary>
    public int ClientsCount => Clients.Count;

    /// <summary>Mouse forwarding status.</summary>
    public BridgeMouseStatus Mouse { get; } = mouse;

    /// <summary>Teensy board status.</summary>
    public BridgeTeensyStatus Teensy { get; } = teensy;

    /// <summary>Controller forwarding status.</summary>
    public BridgeControllerStatus Controller { get; } = controller;

    /// <summary>Steam Input config status.</summary>
    public BridgeSteamInputStatus SteamInput { get; } = steamInput;
}

/// <summary>Connected client status snapshot.</summary>
public sealed class BridgeClientStatus(
    Guid connectionId,
    int processId,
    string profileId,
    uint? steamAppId,
    IReadOnlyList<int> gameProcessIds,
    BridgeClientControllerStatus controller)
{
    /// <summary>Control connection id.</summary>
    public Guid ConnectionId { get; } = connectionId;

    /// <summary>Client process id.</summary>
    public int ProcessId { get; } = processId;

    /// <summary>Client profile id.</summary>
    public string ProfileId { get; } = profileId;

    /// <summary>Steam app id reported by the client.</summary>
    public uint? SteamAppId { get; } = steamAppId;

    /// <summary>Tracked game process ids for this client.</summary>
    public IReadOnlyList<int> GameProcessIds { get; } = gameProcessIds;

    /// <summary>Client-side controller forwarding status.</summary>
    public BridgeClientControllerStatus Controller { get; } = controller;
}

/// <summary>Configured shortcut status snapshot.</summary>
/// <param name="keys">Shortcut key combination.</param>
/// <param name="target">Shortcut target name.</param>
/// <param name="action">Shortcut action.</param>
/// <param name="pressed">Whether the shortcut is currently pressed.</param>
public sealed class BridgeShortcutStatus(
    string keys,
    string target,
    string action,
    bool pressed)
{
    /// <summary>Shortcut key combination.</summary>
    public string Keys { get; } = keys;

    /// <summary>Shortcut target name.</summary>
    public string Target { get; } = target;

    /// <summary>Shortcut action.</summary>
    public string Action { get; } = action;

    /// <summary>Whether the shortcut is currently pressed.</summary>
    public bool Pressed { get; } = pressed;
}

/// <summary>Resolved profile status snapshot.</summary>
public sealed class BridgeProfileStatus(
    string id,
    GameProfile definition,
    bool active,
    int? clientProcessId,
    uint? effectiveSteamAppId,
    IReadOnlyList<int> gameProcessIds)
{
    /// <summary>Profile id.</summary>
    public string Id { get; } = id;

    /// <summary>Profile definition.</summary>
    public GameProfile Definition { get; } = definition;

    /// <summary>Whether this profile is active.</summary>
    public bool Active { get; } = active;

    /// <summary>Connected client process id.</summary>
    public int? ClientProcessId { get; } = clientProcessId;

    /// <summary>Effective Steam app id.</summary>
    public uint? EffectiveSteamAppId { get; } = effectiveSteamAppId;

    /// <summary>Tracked game process ids.</summary>
    public IReadOnlyList<int> GameProcessIds { get; } = gameProcessIds;
}

/// <summary>Server-side mouse forwarding status.</summary>
public sealed class BridgeMouseStatus(string output, bool outputConnected, bool pointerEnabled, bool forwarding)
{
    /// <summary>Selected mouse output.</summary>
    public string Output { get; } = output;

    /// <summary>Whether the selected output is connected.</summary>
    public bool OutputConnected { get; } = outputConnected;

    /// <summary>Whether the pointer shortcut gate is enabled.</summary>
    public bool PointerEnabled { get; } = pointerEnabled;

    /// <summary>Whether mouse input is currently forwarded.</summary>
    public bool Forwarding { get; } = forwarding;
}

/// <summary>Server-side Teensy board status.</summary>
public sealed class BridgeTeensyStatus(string state, string? configuredPort, string? connectedPort)
{
    /// <summary>Connection state, either Connecting or Connected.</summary>
    public string State { get; } = state;

    /// <summary>Configured COM port, or None when auto-discovery is enabled.</summary>
    public string? ConfiguredPort { get; } = configuredPort;

    /// <summary>Connected COM port when a board is connected.</summary>
    public string? ConnectedPort { get; } = connectedPort;

    /// <summary>Whether the Teensy board is currently connected.</summary>
    public bool Connected => string.Equals(State, "Connected", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Server-side controller forwarding status.</summary>
public sealed class BridgeControllerStatus(
    string client,
    int steamControllers,
    int virtualControllers,
    bool forwarding)
{
    /// <summary>Active client state.</summary>
    public string Client { get; } = client;

    /// <summary>Steam Controller route count.</summary>
    public int SteamControllers { get; } = steamControllers;

    /// <summary>Virtual controller output count.</summary>
    public int VirtualControllers { get; } = virtualControllers;

    /// <summary>Whether controller input is currently forwarded.</summary>
    public bool Forwarding { get; } = forwarding;
}

/// <summary>Client-side controller forwarding status.</summary>
public sealed class BridgeClientControllerStatus(bool active, int steamControllers, int virtualControllers)
{
    /// <summary>Whether this client is active.</summary>
    public bool Active { get; } = active;

    /// <summary>Steam Controller route count.</summary>
    public int SteamControllers { get; } = steamControllers;

    /// <summary>Virtual controller output count.</summary>
    public int VirtualControllers { get; } = virtualControllers;
}

/// <summary>Client runtime status snapshot.</summary>
public sealed class BridgeClientRuntimeStatus(BridgeClientControllerStatus controller)
{
    /// <summary>Client-side controller forwarding status.</summary>
    public BridgeClientControllerStatus Controller { get; } = controller;
}

/// <summary>Steam Input config status.</summary>
public sealed class BridgeSteamInputStatus(string? profileId, uint? appId, string? lastError)
{
    /// <summary>Profile whose Steam Input config is currently forced.</summary>
    public string? ProfileId { get; } = profileId;

    /// <summary>Currently forced Steam app id.</summary>
    public uint? AppId { get; } = appId;

    /// <summary>Last Steam Input config error.</summary>
    public string? LastError { get; } = lastError;
}
