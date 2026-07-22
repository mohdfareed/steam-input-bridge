using System.Collections.Generic;
using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Outputs.Controller;
using SteamInputBridge.Outputs.Mouse;
using SteamInputBridge.Shortcuts;

namespace SteamInputBridge.Settings;

/// <summary>Application-owned settings root loaded from the existing SteamInputBridge section.</summary>
public sealed class SteamInputBridgeSettings
{
    /// <summary>Root section for app-owned settings.</summary>
    public const string SectionName = "SteamInputBridge";

    /// <summary>Application logging settings.</summary>
    public LoggingSettings Logging { get; set; } = new();

    /// <summary>VIIPER output settings.</summary>
    public ViiperSettings Viiper { get; set; } = new();

    /// <summary>Teensy output settings.</summary>
    public TeensySettings Teensy { get; set; } = new();

    /// <summary>Steam integration settings.</summary>
    public SteamSettings Steam { get; set; } = new();

    /// <summary>Global keyboard shortcut settings.</summary>
    public Collection<ShortcutEntry> Shortcuts { get; } = [];

    /// <summary>Configured game profiles by profile id.</summary>
    public Dictionary<string, GameProfile> Games { get; } = [];
}

/// <summary>Application logging settings.</summary>
public sealed class LoggingSettings
{
    /// <summary>Configuration section name for logging settings.</summary>
    public const string SectionName = SteamInputBridgeSettings.SectionName + ":Logging";

    /// <summary>Minimum console log level.</summary>
    public LogLevel Level { get; set; } = LogLevel.Information;

    /// <summary>Optional log directory retained for settings compatibility.</summary>
    public string? LogDirectory { get; set; }
}

/// <summary>VIIPER output settings.</summary>
public sealed class ViiperSettings
{
    /// <summary>Configuration section name for VIIPER output settings.</summary>
    public const string SectionName = SteamInputBridgeSettings.SectionName + ":Viiper";

    /// <summary>VIIPER server host.</summary>
    public string Host { get; set; } = "localhost";

    /// <summary>VIIPER server port.</summary>
    public int Port { get; set; } = 3242;
}

/// <summary>Teensy output settings.</summary>
public sealed class TeensySettings
{
    /// <summary>Configuration section name for Teensy output settings.</summary>
    public const string SectionName = SteamInputBridgeSettings.SectionName + ":Teensy";

    /// <summary>Optional COM port name. When empty, the app searches for a likely Teensy serial port.</summary>
    public string? Port { get; set; }

    /// <summary>Directory searched by the tray firmware upload action.</summary>
    public string? FirmwareDirectory { get; set; } = ".";
}

/// <summary>Steam integration settings.</summary>
public sealed class SteamSettings
{
    /// <summary>Configuration section name for Steam integration settings.</summary>
    public const string SectionName = SteamInputBridgeSettings.SectionName + ":Steam";

    /// <summary>Default Steam ROM Manager manifest export path.</summary>
    public string? SrmExportPath { get; set; }
}

/// <summary>Global shortcut binding.</summary>
public sealed class ShortcutEntry
{
    /// <summary>Keyboard combination such as Ctrl+Alt+F13.</summary>
    public string Keys { get; set; } = "";

    /// <summary>Shortcut target controlled by this shortcut.</summary>
    public ShortcutTargetSetting? Target { get; set; }

    /// <summary>State applied when the shortcut is pressed.</summary>
    public ShortcutValue Action { get; set; } = ShortcutValue.Enable;
}

/// <summary>Configuration for one game profile.</summary>
public sealed class GameProfile
{
    /// <summary>Display title.</summary>
    public string Title { get; set; } = "";

    /// <summary>Optional Steam app id used when the client cannot read one from Steam.</summary>
    public uint? SteamAppId { get; set; }

    /// <summary>Optional executable path used to start the game.</summary>
    public string? Executable { get; set; }

    /// <summary>Optional working directory.</summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>Optional process arguments.</summary>
    public string? Arguments { get; set; }

    /// <summary>Processes that identify the receiver game.</summary>
    public Collection<string> ReceiverProcesses { get; } = [];

    /// <summary>Virtual replacement controller output.</summary>
    public ControllerOutput? ControllerOutput { get; set; }

    /// <summary>Virtual pointer output.</summary>
    public MouseOutput? MouseOutput { get; set; }

    /// <summary>Source used for mouse forwarding.</summary>
    public MouseInputMode MouseInput { get; set; } = MouseInputMode.Windows;

    /// <summary>Keyboard shortcuts active only while this profile is active.</summary>
    public Collection<ShortcutEntry> Shortcuts { get; } = [];
}

/// <summary>Source used for mouse forwarding.</summary>
public enum MouseInputMode
{
    /// <summary>Windows mouse input.</summary>
    Windows,

    /// <summary>Steam Input virtual controller state.</summary>
    Steam,
}
