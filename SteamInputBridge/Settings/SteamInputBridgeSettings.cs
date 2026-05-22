using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Settings.Profiles;

namespace SteamInputBridge.Settings;

// MARK: Models
// ============================================================================

/// <summary>Resolved settings file path.</summary>
public sealed record SettingsFile(string Path);

/// <summary>Application-owned settings root.</summary>
public sealed class SteamInputBridgeSettings
{
    /// <summary>Root section for app-owned settings.</summary>
    public const string SectionName = "SteamInputBridge";

    /// <summary>Application logging settings.</summary>
    public LoggingSettings Logging { get; set; } = new();

    /// <summary>VIIPER output settings.</summary>
    public ViiperSettings Viiper { get; set; } = new();

    /// <summary>Steam integration settings.</summary>
    public SteamSettings Steam { get; set; } = new();

    /// <summary>HidHide device firewall settings.</summary>
    public HidHideSettings HidHide { get; set; } = new();

    /// <summary>Global keyboard shortcuts handled by the server.</summary>
    public Collection<ShortcutEntry> Shortcuts { get; } = [];

    /// <summary>Configured game profiles by profile id.</summary>
    public Dictionary<string, GameProfile> Games { get; } = [];
}

/// <summary>Shortcut target controlled by a keyboard shortcut.</summary>
public enum ShortcutTarget
{
    /// <summary>Controller motion contribution.</summary>
    Motion,

    /// <summary>Virtual pointer report forwarding.</summary>
    [SuppressMessage(
        "Naming",
        "CA1720:Identifier contains type name",
        Justification = "Shortcut targets are user-facing settings and Pointer is the intended name.")]
    Pointer,
}

/// <summary>Shortcut target state.</summary>
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

/// <summary>Global shortcut binding.</summary>
public sealed class ShortcutEntry
{
    /// <summary>Display name for diagnostics.</summary>
    public string? Name { get; set; }

    /// <summary>Keyboard combination such as Ctrl+Alt+F13.</summary>
    public string Keys { get; set; } = "";

    /// <summary>Targets controlled by this shortcut.</summary>
    public Collection<ShortcutTarget> Targets { get; } = [];

    /// <summary>State applied when the shortcut is pressed.</summary>
    public ShortcutValue? Value { get; set; }
}

/// <summary>Steam integration settings.</summary>
public sealed class SteamSettings
{
    /// <summary>Configuration section name for Steam settings.</summary>
    public const string SectionName = SteamInputBridgeSettings.SectionName + ":Steam";

    /// <summary>Default Steam ROM Manager manifest export path.</summary>
    public string? SrmExportPath { get; set; }
}

/// <summary>HidHide device firewall settings.</summary>
public sealed class HidHideSettings
{
    /// <summary>Configuration section name for HidHide settings.</summary>
    public const string SectionName = SteamInputBridgeSettings.SectionName + ":HidHide";

    /// <summary>Whether the server should manage HidHide state.</summary>
    public bool Enabled { get; set; }

    /// <summary>HidHide command-line executable path.</summary>
    public string CliPath { get; set; } =
        @"C:\Program Files\Nefarius Software Solutions\HidHide\x64\HidHideCLI.exe";
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

/// <summary>Application logging settings.</summary>
public sealed class LoggingSettings
{
    /// <summary>Configuration section name for logging settings.</summary>
    public const string SectionName = SteamInputBridgeSettings.SectionName + ":Logging";

    /// <summary>Minimum log level.</summary>
    public LogLevel Level { get; set; } = LogLevel.Information;

    /// <summary>Optional log directory.</summary>
    public string? LogDirectory { get; set; }
}
