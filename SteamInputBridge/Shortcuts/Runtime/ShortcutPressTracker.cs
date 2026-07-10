using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace SteamInputBridge.Shortcuts.Runtime;

internal sealed class ShortcutPressTracker(Func<ushort, bool> isKeyDown)
{
    private readonly Dictionary<int, KeyboardShortcut> _shortcuts = [];
    private readonly Dictionary<ushort, int> _pending = [];
    private readonly HashSet<int> _pressed = [];
    private readonly HashSet<ushort> _blockedVirtualKeys = [];
    private readonly List<ushort> _scratchVirtualKeys = [];
    private readonly List<int> _scratchShortcutIds = [];
    private Action<int> _pressedCallback = static _ => { };
    private Action<int> _releasedCallback = static _ => { };

    public bool HasActiveState => _pending.Count != 0 || _pressed.Count != 0 || _blockedVirtualKeys.Count != 0;

    public void Update(
        IReadOnlyList<KeyboardShortcutRegistration> shortcuts,
        Action<int> pressed,
        Action<int> released)
    {
        ArgumentNullException.ThrowIfNull(shortcuts);
        ArgumentNullException.ThrowIfNull(pressed);
        ArgumentNullException.ThrowIfNull(released);

        _shortcuts.Clear();
        _pending.Clear();
        _pressed.Clear();
        _blockedVirtualKeys.Clear();
        _pressedCallback = pressed;
        _releasedCallback = released;

        foreach (KeyboardShortcutRegistration shortcut in shortcuts)
        {
            _shortcuts[shortcut.Id] = shortcut.Shortcut;
        }
    }

    public bool HotkeyPressed(int id)
    {
        if (!_shortcuts.TryGetValue(id, out KeyboardShortcut shortcut) ||
            _blockedVirtualKeys.Contains(shortcut.VirtualKey) ||
            HasPressedShortcutForKey(shortcut.VirtualKey))
        {
            return false;
        }

        if (!HasShortcutVariantForKey(shortcut))
        {
            Press(id);
            return true;
        }

        _pending[shortcut.VirtualKey] = id;
        return true;
    }

    public bool KeyPressed(ushort virtualKey)
    {
        KeyboardShortcutModifiers currentModifiers = CurrentModifiers();
        int? fallbackId = null;

        foreach ((int id, KeyboardShortcut shortcut) in _shortcuts)
        {
            if (shortcut.VirtualKey != virtualKey || !HasRequiredModifiers(shortcut.Modifiers))
            {
                continue;
            }

            if (shortcut.Modifiers == currentModifiers)
            {
                return HotkeyPressed(id);
            }

            fallbackId ??= id;
        }

        return fallbackId.HasValue && HotkeyPressed(fallbackId.Value);
    }

    public void Refresh()
    {
        ClearReleasedBlocks();
        CommitPendingShortcuts();
        RefreshPressedShortcuts();
        ClearReleasedBlocks();
    }

    private void CommitPendingShortcuts()
    {
        _scratchVirtualKeys.Clear();
        foreach (ushort virtualKey in _pending.Keys)
        {
            _scratchVirtualKeys.Add(virtualKey);
        }

        foreach (ushort virtualKey in _scratchVirtualKeys)
        {
            if (!_pending.TryGetValue(virtualKey, out int id))
            {
                continue;
            }

            _ = _pending.Remove(virtualKey);
            if (!_shortcuts.TryGetValue(id, out KeyboardShortcut shortcut) ||
                _blockedVirtualKeys.Contains(virtualKey) ||
                HasPressedShortcutForKey(virtualKey))
            {
                continue;
            }

            int? exactId = FindExactPressedShortcut(virtualKey);
            if (exactId.HasValue)
            {
                Press(exactId.Value);
                continue;
            }

            if (!IsHeld(shortcut))
            {
                Press(id);
                Release(id, blockUntilMainKeyReleased: isKeyDown(shortcut.VirtualKey));
                continue;
            }

            Press(id);
        }
    }

    private void RefreshPressedShortcuts()
    {
        _scratchShortcutIds.Clear();
        foreach (int id in _pressed)
        {
            _scratchShortcutIds.Add(id);
        }

        foreach (int id in _scratchShortcutIds)
        {
            if (!_shortcuts.TryGetValue(id, out KeyboardShortcut shortcut))
            {
                continue;
            }

            if (IsHeld(shortcut))
            {
                continue;
            }

            Release(id, blockUntilMainKeyReleased: isKeyDown(shortcut.VirtualKey));
        }
    }

    private void ClearReleasedBlocks()
    {
        _scratchVirtualKeys.Clear();
        foreach (ushort virtualKey in _blockedVirtualKeys)
        {
            if (!isKeyDown(virtualKey))
            {
                _scratchVirtualKeys.Add(virtualKey);
            }
        }

        foreach (ushort virtualKey in _scratchVirtualKeys)
        {
            _ = _blockedVirtualKeys.Remove(virtualKey);
        }
    }

    private int? FindExactPressedShortcut(ushort virtualKey)
    {
        KeyboardShortcutModifiers currentModifiers = CurrentModifiers();
        foreach ((int id, KeyboardShortcut shortcut) in _shortcuts)
        {
            if (shortcut.VirtualKey == virtualKey &&
                shortcut.Modifiers == currentModifiers &&
                isKeyDown(shortcut.VirtualKey))
            {
                return id;
            }
        }

        return null;
    }

    private bool HasPressedShortcutForKey(ushort virtualKey)
    {
        foreach (int id in _pressed)
        {
            if (_shortcuts.TryGetValue(id, out KeyboardShortcut shortcut) && shortcut.VirtualKey == virtualKey)
            {
                return true;
            }
        }

        return false;
    }

    private bool HasShortcutVariantForKey(KeyboardShortcut shortcut)
    {
        foreach (KeyboardShortcut candidate in _shortcuts.Values)
        {
            if (candidate.VirtualKey == shortcut.VirtualKey && candidate.Modifiers != shortcut.Modifiers)
            {
                return true;
            }
        }

        return false;
    }

    private void Press(int id)
    {
        if (_pressed.Add(id))
        {
            _pressedCallback(id);
        }
    }

    private void Release(int id, bool blockUntilMainKeyReleased)
    {
        if (!_pressed.Remove(id))
        {
            return;
        }

        if (_shortcuts.TryGetValue(id, out KeyboardShortcut shortcut))
        {
            _ = _pending.Remove(shortcut.VirtualKey);
            if (blockUntilMainKeyReleased)
            {
                _ = _blockedVirtualKeys.Add(shortcut.VirtualKey);
            }
        }

        _releasedCallback(id);
    }

    private bool IsHeld(KeyboardShortcut shortcut)
    {
        return isKeyDown(shortcut.VirtualKey) && HasRequiredModifiers(shortcut.Modifiers);
    }

    private KeyboardShortcutModifiers CurrentModifiers()
    {
        KeyboardShortcutModifiers modifiers = KeyboardShortcutModifiers.None;
        if (isKeyDown((ushort)Keys.ControlKey))
        {
            modifiers |= KeyboardShortcutModifiers.Control;
        }

        if (isKeyDown((ushort)Keys.Menu))
        {
            modifiers |= KeyboardShortcutModifiers.Alt;
        }

        if (isKeyDown((ushort)Keys.ShiftKey))
        {
            modifiers |= KeyboardShortcutModifiers.Shift;
        }

        if (isKeyDown((ushort)Keys.LWin) || isKeyDown((ushort)Keys.RWin))
        {
            modifiers |= KeyboardShortcutModifiers.Windows;
        }

        return modifiers;
    }

    private bool HasRequiredModifiers(KeyboardShortcutModifiers modifiers)
    {
        return HasRequiredModifier(modifiers, KeyboardShortcutModifiers.Control, (ushort)Keys.ControlKey) &&
            HasRequiredModifier(modifiers, KeyboardShortcutModifiers.Alt, (ushort)Keys.Menu) &&
            HasRequiredModifier(modifiers, KeyboardShortcutModifiers.Shift, (ushort)Keys.ShiftKey) &&
            HasRequiredWindowsModifier(modifiers);
    }

    private bool HasRequiredModifier(
        KeyboardShortcutModifiers modifiers,
        KeyboardShortcutModifiers modifier,
        ushort virtualKey)
    {
        return (modifiers & modifier) == 0 || isKeyDown(virtualKey);
    }

    private bool HasRequiredWindowsModifier(KeyboardShortcutModifiers modifiers)
    {
        return (modifiers & KeyboardShortcutModifiers.Windows) == 0 ||
            isKeyDown((ushort)Keys.LWin) ||
            isKeyDown((ushort)Keys.RWin);
    }
}
