namespace SteamInputBridge.Shortcuts;

/// <summary>Named shortcut target.</summary>
public enum ShortcutTarget
{
    /// <summary>Controller motion contribution.</summary>
    ControllerMotion,

    /// <summary>Virtual pointer report forwarding.</summary>
    MousePointer,

    /// <summary>System microphone mute state.</summary>
    Microphone,

    /// <summary>Overlay color target.</summary>
    OverlayColor,
}
