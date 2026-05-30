using System.Collections.Generic;
using SteamInputBridge.Hosting.Server.Orchestration;

namespace SteamInputBridge.App.Tray.Menu;

internal sealed partial class AppMenu
{
    private static TrayMenuItem CreateShortcutsMenu(ServerStatus? status)
    {
        if (status is null)
        {
            return TrayMenuItem.Menu("Shortcuts", [TrayMenuItem.Disabled(AppText.WaitingForStatus)]);
        }

        List<TrayMenuItem> items =
        [
            TrayMenuItem.Status(
                "Action color",
                status.Overlay.ActionColor ?? AppText.None,
                status.Overlay.ActionColor is not null),
            CreateHeldShortcutsMenu(status.Shortcuts.HeldShortcuts),
        ];

        return TrayMenuItem.Menu("Shortcuts", items);
    }

    private static TrayMenuItem CreateHeldShortcutsMenu(IReadOnlyList<HeldShortcutStatus> held)
    {
        if (held.Count == 0)
        {
            return TrayMenuItem.Menu("Held", [TrayMenuItem.Disabled(AppText.None)]);
        }

        List<TrayMenuItem> items = [];
        foreach (HeldShortcutStatus shortcut in held)
        {
            items.Add(TrayMenuItem.Status(
                shortcut.Keys,
                $"{shortcut.Value} {string.Join(", ", shortcut.Targets)}",
                isChecked: true));
        }

        return TrayMenuItem.Menu("Held", items);
    }
}
