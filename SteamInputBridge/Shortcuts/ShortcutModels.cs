using System;
using System.Collections.Generic;

namespace SteamInputBridge.Shortcuts;

/// <summary>Keyboard shortcut modifiers.</summary>
[Flags]
internal enum KeyboardShortcutModifiers
{
    /// <summary>No modifier.</summary>
    None = 0,

    /// <summary>Alt key.</summary>
    Alt = 0x0001,

    /// <summary>Control key.</summary>
    Control = 0x0002,

    /// <summary>Shift key.</summary>
    Shift = 0x0004,

    /// <summary>Windows key.</summary>
    Windows = 0x0008,
}

/// <summary>Parsed keyboard shortcut combination.</summary>
internal readonly record struct KeyboardShortcutCombination(
    KeyboardShortcutModifiers Modifiers,
    ushort VirtualKey)
{
    /// <inheritdoc />
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
            ? $"F{virtualKey - 0x70 + 1}"
            : virtualKey is >= 'A' and <= 'Z'
            ? ((char)virtualKey).ToString()
            : virtualKey is >= '0' and <= '9'
            ? ((char)virtualKey).ToString()
            : $"0x{virtualKey:x2}";
    }
}

/// <summary>Registered keyboard shortcut.</summary>
internal sealed record KeyboardShortcutRegistration(
    int Id,
    KeyboardShortcutCombination Combination);

/// <summary>Receives global keyboard shortcut presses.</summary>
internal interface IKeyboardShortcutListener : IDisposable
{
    /// <summary>Replaces the active shortcut registrations.</summary>
    void Update(IReadOnlyList<KeyboardShortcutRegistration> shortcuts, Action<int> pressed);
}
