namespace SteamInputBridge.Shortcuts;

/// <summary>State applied when a shortcut is pressed.</summary>
public enum ShortcutValue
{
    /// <summary>Enable the target.</summary>
    Enable,

    /// <summary>Disable the target.</summary>
    Disable,

    /// <summary>Toggle the target between enabled and disabled.</summary>
    Toggle,
}
