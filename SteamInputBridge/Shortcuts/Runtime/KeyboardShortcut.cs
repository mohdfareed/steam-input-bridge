using System;
using System.Collections.Generic;
using System.Globalization;

namespace SteamInputBridge.Shortcuts.Runtime;

[Flags]
internal enum KeyboardShortcutModifiers
{
    None = 0,
    Control = 0x0001,
    LeftControl = 0x0002,
    RightControl = 0x0004,
    Alt = 0x0008,
    LeftAlt = 0x0010,
    RightAlt = 0x0020,
    Shift = 0x0040,
    LeftShift = 0x0080,
    RightShift = 0x0100,
    Windows = 0x0200,
    LeftWindows = 0x0400,
    RightWindows = 0x0800,
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

        if ((Modifiers & KeyboardShortcutModifiers.LeftControl) != 0)
        {
            parts.Add("LCtrl");
        }

        if ((Modifiers & KeyboardShortcutModifiers.RightControl) != 0)
        {
            parts.Add("RCtrl");
        }

        if ((Modifiers & KeyboardShortcutModifiers.Alt) != 0)
        {
            parts.Add("Alt");
        }

        if ((Modifiers & KeyboardShortcutModifiers.LeftAlt) != 0)
        {
            parts.Add("LAlt");
        }

        if ((Modifiers & KeyboardShortcutModifiers.RightAlt) != 0)
        {
            parts.Add("RAlt");
        }

        if ((Modifiers & KeyboardShortcutModifiers.Shift) != 0)
        {
            parts.Add("Shift");
        }

        if ((Modifiers & KeyboardShortcutModifiers.LeftShift) != 0)
        {
            parts.Add("LShift");
        }

        if ((Modifiers & KeyboardShortcutModifiers.RightShift) != 0)
        {
            parts.Add("RShift");
        }

        if ((Modifiers & KeyboardShortcutModifiers.Windows) != 0)
        {
            parts.Add("Win");
        }

        if ((Modifiers & KeyboardShortcutModifiers.LeftWindows) != 0)
        {
            parts.Add("LWin");
        }

        if ((Modifiers & KeyboardShortcutModifiers.RightWindows) != 0)
        {
            parts.Add("RWin");
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
            "LCTRL" or "LCONTROL" or "LEFTCTRL" or "LEFTCONTROL" => KeyboardShortcutModifiers.LeftControl,
            "RCTRL" or "RCONTROL" or "RIGHTCTRL" or "RIGHTCONTROL" => KeyboardShortcutModifiers.RightControl,
            "ALT" => KeyboardShortcutModifiers.Alt,
            "LALT" or "LEFTALT" => KeyboardShortcutModifiers.LeftAlt,
            "RALT" or "RIGHTALT" => KeyboardShortcutModifiers.RightAlt,
            "SHIFT" => KeyboardShortcutModifiers.Shift,
            "LSHIFT" or "LEFTSHIFT" => KeyboardShortcutModifiers.LeftShift,
            "RSHIFT" or "RIGHTSHIFT" => KeyboardShortcutModifiers.RightShift,
            "WIN" or "WINDOWS" => KeyboardShortcutModifiers.Windows,
            "LWIN" or "LWINDOWS" or "LEFTWIN" or "LEFTWINDOWS" => KeyboardShortcutModifiers.LeftWindows,
            "RWIN" or "RWINDOWS" or "RIGHTWIN" or "RIGHTWINDOWS" => KeyboardShortcutModifiers.RightWindows,
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
