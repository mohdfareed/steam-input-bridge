using System;
using System.Collections.Generic;
using System.Globalization;

namespace SteamInputBridge.Shortcuts.Runtime;

[Flags]
internal enum KeyboardShortcutModifiers
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Windows = 0x0008,
}

internal readonly record struct KeyboardShortcut(KeyboardShortcutModifiers Modifiers, ushort VirtualKey)
{
    public override string ToString()
    {
        List<string> parts = [];
        if ((Modifiers & KeyboardShortcutModifiers.Control) != 0)
        {
            parts.Add("Ctrl");
        }

        if ((Modifiers & KeyboardShortcutModifiers.Alt) != 0)
        {
            parts.Add("Alt");
        }

        if ((Modifiers & KeyboardShortcutModifiers.Shift) != 0)
        {
            parts.Add("Shift");
        }

        if ((Modifiers & KeyboardShortcutModifiers.Windows) != 0)
        {
            parts.Add("Win");
        }

        parts.Add(FormatVirtualKey(VirtualKey));
        return string.Join("+", parts);
    }

    private static string FormatVirtualKey(ushort virtualKey)
    {
        return virtualKey is >= 0x70 and <= 0x87
            ? "F" + (virtualKey - 0x70 + 1).ToString(CultureInfo.InvariantCulture)
            : virtualKey is >= 0x60 and <= 0x69
            ? "Num" + (virtualKey - 0x60).ToString(CultureInfo.InvariantCulture)
            : virtualKey is >= 'A' and <= 'Z'
            ? ((char)virtualKey).ToString()
            : virtualKey is >= '0' and <= '9'
            ? ((char)virtualKey).ToString()
            : virtualKey switch
            {
                0x6A => "Num*",
                0x6B => "Num+",
                0x6D => "Num-",
                0x6E => "Num.",
                0x6F => "Num/",
                0x0D => "Enter",
                0x1B => "Esc",
                0x20 => "Space",
                0x09 => "Tab",
                0x08 => "Backspace",
                _ => "0x" + virtualKey.ToString("X2", CultureInfo.InvariantCulture),
            };
    }
}

internal sealed record KeyboardShortcutRegistration(int Id, KeyboardShortcut Shortcut);

internal static class KeyboardShortcutParser
{
    public static KeyboardShortcut Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        KeyboardShortcutModifiers modifiers = KeyboardShortcutModifiers.None;
        ushort? key = null;
        foreach (string rawPart in value.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (TryParseModifier(rawPart, out KeyboardShortcutModifiers modifier))
            {
                modifiers |= modifier;
                continue;
            }

            if (key.HasValue)
            {
                throw new FormatException($"Shortcut \"{value}\" contains more than one non-modifier key.");
            }

            key = ParseKey(rawPart);
        }

        return key.HasValue
            ? new KeyboardShortcut(modifiers, key.Value)
            : throw new FormatException($"Shortcut \"{value}\" does not contain a key.");
    }

    private static bool TryParseModifier(string value, out KeyboardShortcutModifiers modifier)
    {
        modifier = value.ToUpperInvariant() switch
        {
            "CTRL" or "CONTROL" => KeyboardShortcutModifiers.Control,
            "ALT" => KeyboardShortcutModifiers.Alt,
            "SHIFT" => KeyboardShortcutModifiers.Shift,
            "WIN" or "WINDOWS" => KeyboardShortcutModifiers.Windows,
            _ => KeyboardShortcutModifiers.None,
        };
        return modifier != KeyboardShortcutModifiers.None;
    }

    private static ushort ParseKey(string value)
    {
        string normalized = value.ToUpperInvariant();
        return normalized switch
        {
            [var key] when key is (>= 'A' and <= 'Z') or (>= '0' and <= '9') => key,
            string function when function.StartsWith('F') &&
                int.TryParse(function[1..], NumberStyles.None, CultureInfo.InvariantCulture, out int functionKey) &&
                functionKey is >= 1 and <= 24 => (ushort)(0x70 + functionKey - 1),
            string num when num.StartsWith("NUM", StringComparison.Ordinal) &&
                int.TryParse(num[3..], NumberStyles.None, CultureInfo.InvariantCulture, out int numKey) &&
                numKey is >= 0 and <= 9 => (ushort)(0x60 + numKey),
            "NUM*" => 0x6A,
            "NUM+" => 0x6B,
            "NUM-" => 0x6D,
            "NUM." => 0x6E,
            "NUM/" => 0x6F,
            "ENTER" => 0x0D,
            "ESC" or "ESCAPE" => 0x1B,
            "SPACE" => 0x20,
            "TAB" => 0x09,
            "BACKSPACE" => 0x08,
            _ => throw new FormatException($"Unsupported shortcut key \"{value}\"."),
        };
    }
}
