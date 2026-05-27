using System.Runtime.InteropServices;

namespace SteamInputBridge.Shortcuts;

/// <summary>Reads current keyboard shortcut key state.</summary>
internal static class KeyboardShortcutState
{
    /// <summary>Gets whether every key in the combination is currently down.</summary>
    public static bool IsDown(KeyboardShortcutCombination combination)
    {
        return IsKeyDown(combination.VirtualKey) &&
            HasModifierState(combination.Modifiers, KeyboardShortcutModifiers.Control, 0x11) &&
            HasModifierState(combination.Modifiers, KeyboardShortcutModifiers.Alt, 0x12) &&
            HasModifierState(combination.Modifiers, KeyboardShortcutModifiers.Shift, 0x10) &&
            HasModifierState(combination.Modifiers, KeyboardShortcutModifiers.Windows, 0x5B, 0x5C);
    }

    private static bool HasModifierState(
        KeyboardShortcutModifiers actual,
        KeyboardShortcutModifiers expected,
        ushort virtualKey,
        ushort? alternateVirtualKey = null)
    {
        return (actual & expected) == 0 ||
            IsKeyDown(virtualKey) ||
            (alternateVirtualKey.HasValue && IsKeyDown(alternateVirtualKey.Value));
    }

    internal static bool IsKeyDown(ushort virtualKey)
    {
        return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern short GetAsyncKeyState(int vKey);
}
