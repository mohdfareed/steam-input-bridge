using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
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

    /// <summary>System microphone mute state.</summary>
    Mic,
}

/// <summary>Shortcut target entry from settings.</summary>
[TypeConverter(typeof(ShortcutTargetSpecConverter))]
public readonly record struct ShortcutTargetSpec
{
    private ShortcutTargetSpec(ShortcutTarget? target, string? color)
    {
        Target = target;
        Color = color;
    }

    /// <summary>Named target, such as Motion or Mic.</summary>
    public ShortcutTarget? Target { get; }

    /// <summary>Overlay action-dot color as normalized #RRGGBB.</summary>
    public string? Color { get; }

    /// <summary>Creates a named shortcut target.</summary>
    public static ShortcutTargetSpec Named(ShortcutTarget target)
    {
        return new ShortcutTargetSpec(target, null);
    }

    /// <summary>Creates a color shortcut target.</summary>
    public static ShortcutTargetSpec OverlayColor(string color)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(color);
        return TryNormalizeColor(color, out string? normalized)
            ? new ShortcutTargetSpec(null, normalized)
            : throw new FormatException($"Unsupported shortcut color \"{color}\".");
    }

    /// <summary>Parses a shortcut target setting value.</summary>
    public static ShortcutTargetSpec Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return TryParse(value, out ShortcutTargetSpec target)
            ? target
            : throw new FormatException($"Unsupported shortcut target \"{value}\".");
    }

    /// <summary>Attempts to parse a shortcut target setting value.</summary>
    public static bool TryParse(
        string? value,
        out ShortcutTargetSpec target)
    {
        target = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string trimmed = value.Trim();
        if (TryNormalizeColor(trimmed, out string? color))
        {
            target = new ShortcutTargetSpec(null, color);
            return true;
        }

        if (string.Equals(trimmed, "Voice", StringComparison.OrdinalIgnoreCase))
        {
            target = Named(ShortcutTarget.Mic);
            return true;
        }

        if (Enum.TryParse(trimmed, ignoreCase: true, out ShortcutTarget named))
        {
            target = Named(named);
            return true;
        }

        return false;
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Color ?? Target?.ToString() ?? "";
    }

    private static bool TryNormalizeColor(
        string value,
        [NotNullWhen(true)] out string? normalized)
    {
        normalized = null;
        if (value.Length != 7 || value[0] != '#')
        {
            return false;
        }

        for (int i = 1; i < value.Length; i++)
        {
            char c = value[i];
            bool hex = c is (>= '0' and <= '9') or
                (>= 'a' and <= 'f') or
                (>= 'A' and <= 'F');
            if (!hex)
            {
                return false;
            }
        }

        normalized = value.ToUpperInvariant();
        return true;
    }
}

/// <summary>Converts JSON shortcut target strings to typed shortcut targets.</summary>
public sealed class ShortcutTargetSpecConverter : TypeConverter
{
    /// <inheritdoc />
    public override bool CanConvertFrom(
        ITypeDescriptorContext? context,
        Type sourceType)
    {
        return sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);
    }

    /// <inheritdoc />
    public override object? ConvertFrom(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object value)
    {
        return value is string text
            ? ShortcutTargetSpec.Parse(text)
            : base.ConvertFrom(context, culture, value);
    }
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
    public Collection<ShortcutTargetSpec> Targets { get; } = [];

    /// <summary>State applied when the shortcut is pressed.</summary>
    public ShortcutValue? Value { get; set; }
}

/// <summary>Steam integration settings.</summary>
public sealed class SteamSettings
{
    /// <summary>Default Steam ROM Manager manifest export path.</summary>
    public string? SrmExportPath { get; set; }
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
    /// <summary>Minimum log level.</summary>
    public LogLevel Level { get; set; } = LogLevel.Information;

    /// <summary>Optional log directory.</summary>
    public string? LogDirectory { get; set; }
}
