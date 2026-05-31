using System;
using System.Windows.Forms;

namespace SteamInputBridge.App.Tray;

internal static class TrayMenu
{
    public static ContextMenuStrip Create(Action exit)
    {
        ContextMenuStrip menu = new();
        _ = menu.Items.Add("Exit", null, (_, _) => exit());
        return menu;
    }
}
