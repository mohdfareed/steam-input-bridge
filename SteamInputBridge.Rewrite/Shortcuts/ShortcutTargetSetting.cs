using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace SteamInputBridge.Shortcuts;

/// <summary>Shortcut target setting value.</summary>
[TypeConverter(typeof(ShortcutTargetSettingConverter))]
public readonly record struct ShortcutTargetSetting(ShortcutTarget Target, string? Color)
{
    // MARK: Publics
    // ========================================================================

    /// <summary>Parses a shortcut target setting value.</summary>
    public static ShortcutTargetSetting Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string trimmed = value.Trim();

        return TryNormalizeColor(trimmed, out string? color)
            ? new ShortcutTargetSetting(ShortcutTarget.OverlayColor, color)
            : IsValidTarget(trimmed, out ShortcutTarget target)
            ? new ShortcutTargetSetting(target, null)
            : throw new FormatException($"Unsupported shortcut target \"{value}\".");
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Target == ShortcutTarget.OverlayColor
            ? Color ?? ""
            : Target.ToString();
    }

    // MARK: Implementation
    // ========================================================================

    private static bool IsValidTarget(string value, out ShortcutTarget target)
    {
        return Enum.TryParse(value, ignoreCase: true, out target) && Enum.IsDefined(target) && target != ShortcutTarget.OverlayColor;
    }

    private static bool TryNormalizeColor(string value, [NotNullWhen(true)] out string? normalized)
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

/// <summary>Converts JSON shortcut target strings to shortcut target settings.</summary>
public sealed class ShortcutTargetSettingConverter : TypeConverter
{
    /// <inheritdoc />
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
    {
        return sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);
    }

    /// <inheritdoc />
    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        return value is string text
            ? ShortcutTargetSetting.Parse(text)
            : base.ConvertFrom(context, culture, value);
    }
}
