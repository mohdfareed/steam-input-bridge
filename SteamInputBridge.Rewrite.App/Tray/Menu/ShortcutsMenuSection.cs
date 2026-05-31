using System.Collections.Generic;
using System.Windows.Forms;
using SteamInputBridge.Hosting;

namespace SteamInputBridge.App.Tray.Menu;

internal sealed class ShortcutsMenuSection
{
    private readonly Dictionary<string, ShortcutMenuBinding> _shortcuts = [];

    // MARK: Publics
    // ========================================================================

    public ToolStripMenuItem Build(IReadOnlyList<BridgeShortcutStatus> shortcuts)
    {
        _shortcuts.Clear();
        ToolStripMenuItem menu = TrayMenuItems.Menu("Shortcuts");
        if (shortcuts.Count == 0)
        {
            _ = menu.DropDownItems.Add(TrayMenuItems.Disabled("None"));
            return menu;
        }

        foreach (BridgeShortcutStatus shortcut in shortcuts)
        {
            _ = menu.DropDownItems.Add(CreateShortcutMenu(shortcut));
        }

        return menu;
    }

    public void Update(IReadOnlyList<BridgeShortcutStatus> shortcuts)
    {
        foreach (BridgeShortcutStatus shortcut in shortcuts)
        {
            if (_shortcuts.TryGetValue(shortcut.Keys, out ShortcutMenuBinding? items))
            {
                items.Update(shortcut);
            }
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
                string.Join('\n', previous[i].Targets) != string.Join('\n', current[i].Targets))
            {
                return true;
            }
        }

        return false;
    }

    // MARK: Build
    // ========================================================================

    private ToolStripMenuItem CreateShortcutMenu(BridgeShortcutStatus shortcut)
    {
        ToolStripMenuItem menu = TrayMenuItems.Menu(shortcut.Keys);
        TrayMenuItems.SetCheckMark(menu, shortcut.Pressed);
        ToolStripMenuItem pressed = TrayMenuItems.Item("Pressed", TrayMenuItems.Enabled(shortcut.Pressed));

        _ = menu.DropDownItems.Add(TrayMenuItems.Item("Targets", string.Join(", ", shortcut.Targets)));
        _ = menu.DropDownItems.Add(TrayMenuItems.Item("Action", shortcut.Action));
        _ = menu.DropDownItems.Add(pressed);

        _shortcuts[shortcut.Keys] = new(menu, pressed);
        return menu;
    }

    private sealed record ShortcutMenuBinding(ToolStripMenuItem Menu, ToolStripMenuItem Pressed)
    {
        public void Update(BridgeShortcutStatus shortcut)
        {
            TrayMenuItems.SetCheckMark(Menu, shortcut.Pressed);
            TrayMenuItems.SetValue(Pressed, TrayMenuItems.Enabled(shortcut.Pressed));
        }
    }
}
