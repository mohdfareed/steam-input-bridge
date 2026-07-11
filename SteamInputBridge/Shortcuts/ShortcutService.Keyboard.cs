using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Forms;
using SteamInputBridge.Shortcuts.Runtime;

namespace SteamInputBridge.Shortcuts;

public sealed partial class ShortcutService
{
    private static readonly long ModifierOrderToleranceTicks = Stopwatch.Frequency * 5 / 1000;
    private const long AlreadyDownTimestamp = long.MinValue / 2;

    // MARK: Keyboard State
    // ============================================================================

    private void SeedCurrentlyDownKeys()
    {
        _downKeys.Clear();
        _downAt.Clear();
        foreach (KeyboardShortcut shortcut in _shortcutKeys.Values)
        {
            if (_listener.IsKeyDown(shortcut.VirtualKey))
            {
                _ = _downKeys.Add(shortcut.VirtualKey);
                _downAt[shortcut.VirtualKey] = AlreadyDownTimestamp;
            }
        }

        AddIfDown(Keys.ControlKey);
        AddIfDown(Keys.LControlKey);
        AddIfDown(Keys.RControlKey);
        AddIfDown(Keys.Menu);
        AddIfDown(Keys.LMenu);
        AddIfDown(Keys.RMenu);
        AddIfDown(Keys.ShiftKey);
        AddIfDown(Keys.LShiftKey);
        AddIfDown(Keys.RShiftKey);
        AddIfDown(Keys.LWin);
        AddIfDown(Keys.RWin);
    }

    private void ReconcileModifierKeys(long now)
    {
        ReconcileModifierKey(Keys.ControlKey, now);
        ReconcileModifierKey(Keys.LControlKey, now);
        ReconcileModifierKey(Keys.RControlKey, now);
        ReconcileModifierKey(Keys.Menu, now);
        ReconcileModifierKey(Keys.LMenu, now);
        ReconcileModifierKey(Keys.RMenu, now);
        ReconcileModifierKey(Keys.ShiftKey, now);
        ReconcileModifierKey(Keys.LShiftKey, now);
        ReconcileModifierKey(Keys.RShiftKey, now);
        ReconcileModifierKey(Keys.LWin, now);
        ReconcileModifierKey(Keys.RWin, now);
    }

    private void ReconcileModifierKey(Keys key, long now)
    {
        ushort virtualKey = (ushort)key;
        if (_listener.IsKeyDown(virtualKey))
        {
            if (_downKeys.Add(virtualKey))
            {
                _downAt[virtualKey] = now;
            }

            return;
        }

        _ = _downKeys.Remove(virtualKey);
        _ = _downAt.Remove(virtualKey);
    }

    private void AddIfDown(Keys key)
    {
        ushort virtualKey = (ushort)key;
        if (_listener.IsKeyDown(virtualKey))
        {
            _ = _downKeys.Add(virtualKey);
            _downAt[virtualKey] = AlreadyDownTimestamp;
        }
    }

    private void ClearReleasedModifier(ushort virtualKey)
    {
        Keys key = (Keys)virtualKey;
        if (key is Keys.ControlKey)
        {
            ClearKey(Keys.ControlKey);
            ClearKey(Keys.LControlKey);
            ClearKey(Keys.RControlKey);
        }
        else if (key is Keys.LControlKey or Keys.RControlKey)
        {
            ClearKey(key);
            ClearKey(Keys.ControlKey);
        }
        else if (key is Keys.Menu)
        {
            ClearKey(Keys.Menu);
            ClearKey(Keys.LMenu);
            ClearKey(Keys.RMenu);
        }
        else if (key is Keys.LMenu or Keys.RMenu)
        {
            ClearKey(key);
            ClearKey(Keys.Menu);
        }
        else if (key is Keys.ShiftKey)
        {
            ClearKey(Keys.ShiftKey);
            ClearKey(Keys.LShiftKey);
            ClearKey(Keys.RShiftKey);
        }
        else if (key is Keys.LShiftKey or Keys.RShiftKey)
        {
            ClearKey(key);
            ClearKey(Keys.ShiftKey);
        }
        else if (key is Keys.LWin or Keys.RWin)
        {
            ClearKey(key);
        }
    }

    private void ClearKey(Keys key)
    {
        ushort virtualKey = (ushort)key;
        _ = _downKeys.Remove(virtualKey);
        _ = _downAt.Remove(virtualKey);
    }

    // MARK: Matching
    // ============================================================================

    private bool ShortcutMatchesCurrentState(KeyboardShortcut shortcut)
    {
        return _downKeys.Contains(shortcut.VirtualKey) &&
            ModifiersMatch(shortcut.Modifiers) &&
            ModifierOrderAllows(shortcut);
    }

    private bool ShortcutRemainsHeld(KeyboardShortcut shortcut)
    {
        return _downKeys.Contains(shortcut.VirtualKey) && RequiredModifiersAreDown(shortcut.Modifiers);
    }

    private bool DesiredHasShortcutForKey(ushort virtualKey)
    {
        foreach (int id in _desiredShortcuts)
        {
            if (_shortcutKeys.TryGetValue(id, out KeyboardShortcut shortcut) && shortcut.VirtualKey == virtualKey)
            {
                return true;
            }
        }

        return false;
    }

    private bool ModifiersMatch(KeyboardShortcutModifiers modifiers)
    {
        return ModifierFamilyMatches(
                modifiers,
                KeyboardShortcutModifiers.Control,
                KeyboardShortcutModifiers.LeftControl,
                KeyboardShortcutModifiers.RightControl,
                Keys.ControlKey,
                Keys.LControlKey,
                Keys.RControlKey) &&
            ModifierFamilyMatches(
                modifiers,
                KeyboardShortcutModifiers.Alt,
                KeyboardShortcutModifiers.LeftAlt,
                KeyboardShortcutModifiers.RightAlt,
                Keys.Menu,
                Keys.LMenu,
                Keys.RMenu) &&
            ModifierFamilyMatches(
                modifiers,
                KeyboardShortcutModifiers.Shift,
                KeyboardShortcutModifiers.LeftShift,
                KeyboardShortcutModifiers.RightShift,
                Keys.ShiftKey,
                Keys.LShiftKey,
                Keys.RShiftKey) &&
            ModifierFamilyMatches(
                modifiers,
                KeyboardShortcutModifiers.Windows,
                KeyboardShortcutModifiers.LeftWindows,
                KeyboardShortcutModifiers.RightWindows,
                genericKey: null,
                Keys.LWin,
                Keys.RWin);
    }

    private bool ModifierFamilyMatches(
        KeyboardShortcutModifiers modifiers,
        KeyboardShortcutModifiers genericModifier,
        KeyboardShortcutModifiers leftModifier,
        KeyboardShortcutModifiers rightModifier,
        Keys? genericKey,
        Keys leftKey,
        Keys rightKey)
    {
        bool requiresGeneric = (modifiers & genericModifier) != 0;
        bool requiresLeft = (modifiers & leftModifier) != 0;
        bool requiresRight = (modifiers & rightModifier) != 0;
        bool anyRequired = requiresGeneric || requiresLeft || requiresRight;
        bool genericDown = genericKey.HasValue && IsDown(genericKey.Value);
        bool leftDown = IsDown(leftKey);
        bool rightDown = IsDown(rightKey);

        return !anyRequired
            ? !genericDown && !leftDown && !rightDown
            : requiresGeneric
            ? genericDown || leftDown || rightDown
            : (!requiresLeft || leftDown) &&
                (!requiresRight || rightDown) &&
                (requiresLeft || !leftDown) &&
                (requiresRight || !rightDown);
    }

    private bool RequiredModifiersAreDown(KeyboardShortcutModifiers modifiers)
    {
        return RequiredModifierFamilyIsDown(
                modifiers,
                KeyboardShortcutModifiers.Control,
                KeyboardShortcutModifiers.LeftControl,
                KeyboardShortcutModifiers.RightControl,
                Keys.ControlKey,
                Keys.LControlKey,
                Keys.RControlKey) &&
            RequiredModifierFamilyIsDown(
                modifiers,
                KeyboardShortcutModifiers.Alt,
                KeyboardShortcutModifiers.LeftAlt,
                KeyboardShortcutModifiers.RightAlt,
                Keys.Menu,
                Keys.LMenu,
                Keys.RMenu) &&
            RequiredModifierFamilyIsDown(
                modifiers,
                KeyboardShortcutModifiers.Shift,
                KeyboardShortcutModifiers.LeftShift,
                KeyboardShortcutModifiers.RightShift,
                Keys.ShiftKey,
                Keys.LShiftKey,
                Keys.RShiftKey) &&
            RequiredModifierFamilyIsDown(
                modifiers,
                KeyboardShortcutModifiers.Windows,
                KeyboardShortcutModifiers.LeftWindows,
                KeyboardShortcutModifiers.RightWindows,
                genericKey: null,
                Keys.LWin,
                Keys.RWin);
    }

    private bool RequiredModifierFamilyIsDown(
        KeyboardShortcutModifiers modifiers,
        KeyboardShortcutModifiers genericModifier,
        KeyboardShortcutModifiers leftModifier,
        KeyboardShortcutModifiers rightModifier,
        Keys? genericKey,
        Keys leftKey,
        Keys rightKey)
    {
        bool requiresGeneric = (modifiers & genericModifier) != 0;
        bool requiresLeft = (modifiers & leftModifier) != 0;
        bool requiresRight = (modifiers & rightModifier) != 0;
        return (!requiresGeneric && !requiresLeft && !requiresRight) ||
            (requiresGeneric
            ? (genericKey.HasValue && IsDown(genericKey.Value)) || IsDown(leftKey) || IsDown(rightKey)
            : (!requiresLeft || IsDown(leftKey)) && (!requiresRight || IsDown(rightKey)));
    }

    private bool ModifierOrderAllows(KeyboardShortcut shortcut)
    {
        return _downAt.TryGetValue(shortcut.VirtualKey, out long mainDownAt) &&
            ModifierFamilyOrderAllows(
                shortcut.Modifiers,
                KeyboardShortcutModifiers.Control,
                KeyboardShortcutModifiers.LeftControl,
                KeyboardShortcutModifiers.RightControl,
                Keys.ControlKey,
                Keys.LControlKey,
                Keys.RControlKey,
                mainDownAt) &&
            ModifierFamilyOrderAllows(
                shortcut.Modifiers,
                KeyboardShortcutModifiers.Alt,
                KeyboardShortcutModifiers.LeftAlt,
                KeyboardShortcutModifiers.RightAlt,
                Keys.Menu,
                Keys.LMenu,
                Keys.RMenu,
                mainDownAt) &&
            ModifierFamilyOrderAllows(
                shortcut.Modifiers,
                KeyboardShortcutModifiers.Shift,
                KeyboardShortcutModifiers.LeftShift,
                KeyboardShortcutModifiers.RightShift,
                Keys.ShiftKey,
                Keys.LShiftKey,
                Keys.RShiftKey,
                mainDownAt) &&
            ModifierFamilyOrderAllows(
                shortcut.Modifiers,
                KeyboardShortcutModifiers.Windows,
                KeyboardShortcutModifiers.LeftWindows,
                KeyboardShortcutModifiers.RightWindows,
                genericKey: null,
                Keys.LWin,
                Keys.RWin,
                mainDownAt);
    }

    private bool ModifierFamilyOrderAllows(
        KeyboardShortcutModifiers modifiers,
        KeyboardShortcutModifiers genericModifier,
        KeyboardShortcutModifiers leftModifier,
        KeyboardShortcutModifiers rightModifier,
        Keys? genericKey,
        Keys leftKey,
        Keys rightKey,
        long mainDownAt)
    {
        bool requiresGeneric = (modifiers & genericModifier) != 0;
        bool requiresLeft = (modifiers & leftModifier) != 0;
        bool requiresRight = (modifiers & rightModifier) != 0;
        return (!requiresGeneric && !requiresLeft && !requiresRight) ||
            (requiresGeneric
            ? (genericKey.HasValue && ModifierKeyOrderAllows(genericKey.Value, mainDownAt)) ||
                ModifierKeyOrderAllows(leftKey, mainDownAt) ||
                ModifierKeyOrderAllows(rightKey, mainDownAt)
            : (!requiresLeft || ModifierKeyOrderAllows(leftKey, mainDownAt)) &&
                (!requiresRight || ModifierKeyOrderAllows(rightKey, mainDownAt)));
    }

    private bool ModifierKeyOrderAllows(Keys key, long mainDownAt)
    {
        ushort virtualKey = (ushort)key;
        return _downKeys.Contains(virtualKey) &&
            _downAt.TryGetValue(virtualKey, out long modifierDownAt) &&
            (modifierDownAt <= mainDownAt || modifierDownAt - mainDownAt <= ModifierOrderToleranceTicks);
    }

    private bool IsDown(Keys key)
    {
        return _downKeys.Contains((ushort)key);
    }

    private static bool IsModifierKey(ushort virtualKey)
    {
        return (Keys)virtualKey is
            Keys.ControlKey or Keys.LControlKey or Keys.RControlKey or
            Keys.Menu or Keys.LMenu or Keys.RMenu or
            Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey or
            Keys.LWin or Keys.RWin;
    }

    private static void AddModifierKeys(HashSet<ushort> keys)
    {
        _ = keys.Add((ushort)Keys.ControlKey);
        _ = keys.Add((ushort)Keys.LControlKey);
        _ = keys.Add((ushort)Keys.RControlKey);
        _ = keys.Add((ushort)Keys.Menu);
        _ = keys.Add((ushort)Keys.LMenu);
        _ = keys.Add((ushort)Keys.RMenu);
        _ = keys.Add((ushort)Keys.ShiftKey);
        _ = keys.Add((ushort)Keys.LShiftKey);
        _ = keys.Add((ushort)Keys.RShiftKey);
        _ = keys.Add((ushort)Keys.LWin);
        _ = keys.Add((ushort)Keys.RWin);
    }
}
