using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;
using SteamInputBridge.Hosting;

namespace SteamInputBridge.App.Tray.Menu;

internal sealed class ShortcutsMenuSection
{
    private readonly Dictionary<string, ShortcutMenuBinding> _shortcuts = [];
    private ToolStripMenuItem? _teensy;
    private ToolStripMenuItem? _teensyStatus;

    // MARK: Publics
    // ========================================================================

    public ToolStripMenuItem Build(
        IReadOnlyList<BridgeShortcutStatus> shortcuts,
        BridgeTeensyStatus teensy,
        Func<Task> uploadTeensyFirmware,
        Action<Exception> onError)
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
                _ = menu.DropDownItems.Add(CreateShortcutMenu(shortcut));
            }
        }

        _ = menu.DropDownItems.Add(new ToolStripSeparator());
        _ = menu.DropDownItems.Add(CreateTeensyMenu(teensy, uploadTeensyFirmware, onError));
        return menu;
    }

    public void Update(IReadOnlyList<BridgeShortcutStatus> shortcuts, BridgeTeensyStatus teensy)
    {
        foreach (BridgeShortcutStatus shortcut in shortcuts)
        {
            if (_shortcuts.TryGetValue(shortcut.Keys, out ShortcutMenuBinding? items))
            {
                items.Update(shortcut);
            }
        }

        SetTeensy(teensy);
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
        ToolStripMenuItem pressed = TrayMenuItems.Item("Pressed", TrayMenuItems.YesNo(shortcut.Pressed));
        TrayMenuItems.SetCheckMark(pressed, shortcut.Pressed);

        _ = menu.DropDownItems.Add(TrayMenuItems.Item("Targets", string.Join(", ", shortcut.Targets)));
        _ = menu.DropDownItems.Add(TrayMenuItems.Item("Action", shortcut.Action));
        _ = menu.DropDownItems.Add(pressed);

        _shortcuts[shortcut.Keys] = new(menu, pressed);
        return menu;
    }

    private ToolStripMenuItem CreateTeensyMenu(
        BridgeTeensyStatus teensy,
        Func<Task> uploadTeensyFirmware,
        Action<Exception> onError)
    {
        _teensy = TrayMenuItems.Menu("Teensy");
        TrayMenuItems.SetCheckMark(_teensy, teensy.Connected);
        _teensyStatus = TrayMenuItems.Item("Status", FormatTeensy(teensy));
        TrayMenuItems.SetCheckMark(_teensyStatus, teensy.Connected);
        _ = _teensy.DropDownItems.Add(_teensyStatus);
        _ = _teensy.DropDownItems.Add(TrayMenuItems.ActionItem("Upload firmware...", uploadTeensyFirmware, onError));
        return _teensy;
    }

    private void SetTeensy(BridgeTeensyStatus teensy)
    {
        if (_teensy is not null)
        {
            TrayMenuItems.SetCheckMark(_teensy, teensy.Connected);
        }

        if (_teensyStatus is not null)
        {
            TrayMenuItems.SetValue(_teensyStatus, FormatTeensy(teensy));
            TrayMenuItems.SetCheckMark(_teensyStatus, teensy.Connected);
        }
    }

    private static string FormatTeensy(BridgeTeensyStatus status)
    {
        return status.Connected && !string.IsNullOrWhiteSpace(status.ConnectedPort)
            ? $"Connected: {status.ConnectedPort}"
            : status.State;
    }

    private sealed record ShortcutMenuBinding(ToolStripMenuItem Menu, ToolStripMenuItem Pressed)
    {
        public void Update(BridgeShortcutStatus shortcut)
        {
            TrayMenuItems.SetCheckMark(Menu, shortcut.Pressed);
            TrayMenuItems.SetValue(Pressed, TrayMenuItems.YesNo(shortcut.Pressed));
            TrayMenuItems.SetCheckMark(Pressed, shortcut.Pressed);
        }
    }
}
