namespace SteamInputBridge.Steam;

/// <summary>Steam game entry source.</summary>
public enum SteamGameKind
{
    /// <summary>Steam app installed from a Steam library manifest.</summary>
    SteamApp,

    /// <summary>Non-Steam shortcut added to the user's Steam library.</summary>
    NonSteamShortcut,
}

/// <summary>Game entry known to Steam.</summary>
public sealed record SteamGame
{
    /// <summary>Gets the app id used by Steam for this entry.</summary>
    public required uint AppId { get; init; }

    /// <summary>Gets the display name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets whether this entry came from a Steam app manifest or non-Steam shortcut.</summary>
    public required SteamGameKind Kind { get; init; }

    /// <summary>Gets the local install, start, or executable path, when known.</summary>
    public string? LocalPath { get; init; }
}
