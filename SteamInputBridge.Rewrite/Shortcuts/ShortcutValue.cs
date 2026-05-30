namespace SteamInputBridge.Shortcuts;

/// <summary>State applied when a shortcut is pressed.</summary>
public enum ShortcutValue
{
    /// <summary>Enable the target.</summary>
    Enabled,

    /// <summary>Disable the target.</summary>
    Disabled,

    /// <summary>Enable the target while the shortcut is held.</summary>
    HoldEnabled,

    /// <summary>Disable the target while the shortcut is held.</summary>
    HoldDisabled,

    /// <summary>Toggle the target between enabled and disabled.</summary>
    Toggle,
}
