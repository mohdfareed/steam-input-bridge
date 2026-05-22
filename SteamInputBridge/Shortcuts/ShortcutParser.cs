using System;

namespace SteamInputBridge.Shortcuts;

/// <summary>Parses keyboard shortcut combinations.</summary>
internal static class KeyboardShortcutParser
{
    /// <summary>Parses a key combination such as Ctrl+Alt+F13.</summary>
    public static KeyboardShortcutCombination Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        KeyboardShortcutModifiers modifiers = KeyboardShortcutModifiers.None;
        ushort? key = null;
        foreach (string part in value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (TryParseModifier(part, out KeyboardShortcutModifiers modifier))
            {
                modifiers |= modifier;
                continue;
            }

            if (key.HasValue)
            {
                throw new FormatException($"Shortcut \"{value}\" contains more than one non-modifier key.");
            }

            key = ParseVirtualKey(part);
        }

        return key.HasValue
            ? new KeyboardShortcutCombination(modifiers, key.Value)
            : throw new FormatException($"Shortcut \"{value}\" does not contain a key.");
    }

    private static bool TryParseModifier(string value, out KeyboardShortcutModifiers modifier)
    {
        if (string.Equals(value, "ctrl", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "control", StringComparison.OrdinalIgnoreCase))
        {
            modifier = KeyboardShortcutModifiers.Control;
            return true;
        }

        if (string.Equals(value, "alt", StringComparison.OrdinalIgnoreCase))
        {
            modifier = KeyboardShortcutModifiers.Alt;
            return true;
        }

        if (string.Equals(value, "shift", StringComparison.OrdinalIgnoreCase))
        {
            modifier = KeyboardShortcutModifiers.Shift;
            return true;
        }

        if (string.Equals(value, "win", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "windows", StringComparison.OrdinalIgnoreCase))
        {
            modifier = KeyboardShortcutModifiers.Windows;
            return true;
        }

        modifier = KeyboardShortcutModifiers.None;
        return false;
    }

    private static ushort ParseVirtualKey(string value)
    {
        if (value.Length == 1)
        {
            char c = char.ToUpperInvariant(value[0]);
            if (c is (>= 'A' and <= 'Z') or (>= '0' and <= '9'))
            {
                return c;
            }
        }

        return value.Length is 2 or 3 &&
            value[0] is 'F' or 'f' &&
            int.TryParse(value[1..], out int functionKey) &&
            functionKey is >= 1 and <= 24
            ? checked((ushort)(0x70 + functionKey - 1))
            : value.ToUpperInvariant() switch
            {
                "NUM0" or "NUMPAD0" => (ushort)0x60,
                "NUM1" or "NUMPAD1" => (ushort)0x61,
                "NUM2" or "NUMPAD2" => (ushort)0x62,
                "NUM3" or "NUMPAD3" => (ushort)0x63,
                "NUM4" or "NUMPAD4" => (ushort)0x64,
                "NUM5" or "NUMPAD5" => (ushort)0x65,
                "NUM6" or "NUMPAD6" => (ushort)0x66,
                "NUM7" or "NUMPAD7" => (ushort)0x67,
                "NUM8" or "NUMPAD8" => (ushort)0x68,
                "NUM9" or "NUMPAD9" => (ushort)0x69,
                "ENTER" => (ushort)0x0D,
                "ESC" or "ESCAPE" => (ushort)0x1B,
                "SPACE" => (ushort)0x20,
                "TAB" => (ushort)0x09,
                "BACKSPACE" => (ushort)0x08,
                _ => throw new FormatException($"Unsupported shortcut key \"{value}\"."),
            };
    }
}
