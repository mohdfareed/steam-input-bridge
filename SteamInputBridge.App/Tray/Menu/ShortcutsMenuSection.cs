using System;
using System.Collections.Generic;
using System.Windows.Forms;
using SteamInputBridge.Hosting;
using SteamInputBridge.Settings;

namespace SteamInputBridge.App.Tray.Menu;

internal sealed class ShortcutsMenuSection
{
    private readonly Dictionary<(string Keys, string Target, string Action), ShortcutMenuBinding> _shortcuts = [];

    // MARK: Publics
    // ========================================================================

    public ToolStripMenuItem Build(IReadOnlyList<BridgeShortcutStatus> shortcuts)
    {
        _shortcuts.Clear();
        ToolStripMenuItem menu = TrayMenuItems.Menu("Shortcuts");
        if (shortcuts.Count == 0)
        {
            _ = menu.DropDownItems.Add(TrayMenuItems.Disabled("None"));
        }
        else
        {
            foreach (BridgeShortcutStatus shortcut in shortcuts)
            {
                _ = menu.DropDownItems.Add(CreateShortcutMenu(
                    shortcut.Keys,
                    shortcut.Target,
                    shortcut.Action,
                    shortcut.Pressed));
            }
        }

        return menu;
    }

    public ToolStripMenuItem BuildProfile(
        IReadOnlyList<ShortcutEntry> shortcuts,
        IReadOnlyList<BridgeShortcutStatus> activeShortcuts,
        bool active)
    {
        _shortcuts.Clear();
        ToolStripMenuItem menu = TrayMenuItems.Menu("Shortcuts");
        if (shortcuts.Count == 0)
        {
            _ = menu.DropDownItems.Add(TrayMenuItems.Disabled("None"));
            return menu;
        }

        foreach (ShortcutEntry shortcut in shortcuts)
        {
            if (!shortcut.Target.HasValue)
            {
                continue;
            }

            string target = shortcut.Target.Value.ToString();
            string action = shortcut.Action.ToString();
            _ = menu.DropDownItems.Add(CreateShortcutMenu(
                shortcut.Keys,
                target,
                action,
                active && IsPressed(activeShortcuts, shortcut.Keys, target, action)));
        }

        if (menu.DropDownItems.Count == 0)
        {
            _ = menu.DropDownItems.Add(TrayMenuItems.Disabled("None"));
        }

        return menu;
    }

    public void Update(IReadOnlyList<BridgeShortcutStatus> shortcuts)
    {
        foreach (BridgeShortcutStatus shortcut in shortcuts)
        {
            UpdateShortcut(shortcut.Keys, shortcut.Target, shortcut.Action, shortcut.Pressed);
        }
    }

    public void UpdateProfile(
        IReadOnlyList<ShortcutEntry> shortcuts,
        IReadOnlyList<BridgeShortcutStatus> activeShortcuts,
        bool active)
    {
        foreach (ShortcutEntry shortcut in shortcuts)
        {
            if (!shortcut.Target.HasValue)
            {
                continue;
            }

            string target = shortcut.Target.Value.ToString();
            string action = shortcut.Action.ToString();
            UpdateShortcut(
                shortcut.Keys,
                target,
                action,
                active && IsPressed(activeShortcuts, shortcut.Keys, target, action));
        }
    }

    public static bool ShapeChanged(
        IReadOnlyList<BridgeShortcutStatus> previous,
        IReadOnlyList<BridgeShortcutStatus> current)
    {
        if (previous.Count != current.Count)
        {
            return true;
        }

        for (int i = 0; i < previous.Count; i++)
        {
            if (previous[i].Keys != current[i].Keys ||
                previous[i].Action != current[i].Action ||
                previous[i].Target != current[i].Target)
            {
                return true;
            }
        }

        return false;
    }

    // MARK: Build
    // ========================================================================

    private ToolStripMenuItem CreateShortcutMenu(string keys, string target, string action, bool pressed)
    {
        ToolStripMenuItem menu = TrayMenuItems.Menu(keys);
        TrayMenuItems.SetCheckMark(menu, pressed);
        ToolStripMenuItem pressedItem = TrayMenuItems.Item("Pressed", TrayMenuItems.YesNo(pressed));
        TrayMenuItems.SetCheckMark(pressedItem, pressed);

        _ = menu.DropDownItems.Add(TrayMenuItems.Item("Target", target));
        _ = menu.DropDownItems.Add(TrayMenuItems.Item("Action", action));
        _ = menu.DropDownItems.Add(pressedItem);

        _shortcuts[(keys, target, action)] = new(menu, pressedItem);
        return menu;
    }

    private void UpdateShortcut(string keys, string target, string action, bool pressed)
    {
        if (_shortcuts.TryGetValue((keys, target, action), out ShortcutMenuBinding? items))
        {
            items.Update(pressed);
        }
    }

    private static bool IsPressed(
        IReadOnlyList<BridgeShortcutStatus> shortcuts,
        string keys,
        string target,
        string action)
    {
        foreach (BridgeShortcutStatus shortcut in shortcuts)
        {
            if (shortcut.Pressed &&
                string.Equals(shortcut.Keys, keys, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(shortcut.Target, target, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(shortcut.Action, action, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private sealed record ShortcutMenuBinding(ToolStripMenuItem Menu, ToolStripMenuItem Pressed)
    {
        public void Update(bool pressed)
        {
            TrayMenuItems.SetCheckMark(Menu, pressed);
            TrayMenuItems.SetValue(Pressed, TrayMenuItems.YesNo(pressed));
            TrayMenuItems.SetCheckMark(Pressed, pressed);
        }
    }
}
