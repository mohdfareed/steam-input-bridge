using System;
using System.Collections.Generic;
using System.Windows.Forms;
using SteamInputBridge.Hosting;
using SteamInputBridge.Profiles;

namespace SteamInputBridge.App.Tray.Menu;

internal sealed class TrayMenu(TrayActions actions, Action restart, Action exit, Action<Exception> onError)
{
    public ContextMenuStrip Menu { get; } = new()
    {
        RenderMode = ToolStripRenderMode.System,
    };

    // MARK: Publics
    // ========================================================================

    public void Rebuild(IReadOnlyList<ProfileStatus> profiles, BridgeServerStatus status, bool isStartupEnabled)
    {
        Menu.SuspendLayout();
        try
        {
            Menu.Items.Clear();
            _ = Menu.Items.Add(CreateProfilesMenu(profiles));
            _ = Menu.Items.Add(CreateShortcutsMenu(status));
            _ = Menu.Items.Add(new ToolStripSeparator());
            _ = Menu.Items.Add(TrayMenuItems.ActionItem(
                "Open Steam Controller desktop config",
                actions.OpenDesktopSteamInputConfigAsync,
                onError));
            _ = Menu.Items.Add(TrayMenuItems.ActionItem("Export SRM manifest", actions.ExportSrmManifest, onError));
            _ = Menu.Items.Add(new ToolStripSeparator());
            _ = Menu.Items.Add(TrayMenuItems.ActionItem("Open settings", actions.OpenSettings, onError));
            _ = Menu.Items.Add(TrayMenuItems.ActionItem("Open logs", actions.OpenLogs, onError));
            _ = Menu.Items.Add(new ToolStripSeparator());
            _ = Menu.Items.Add(CreateStartupItem(isStartupEnabled));
            _ = Menu.Items.Add(TrayMenuItems.ActionItem("Restart", restart, onError));
            _ = Menu.Items.Add(TrayMenuItems.ActionItem("Exit", exit, onError));
        }
        finally
        {
            Menu.ResumeLayout();
        }
    }

    // MARK: Implementation
    // ========================================================================

    private static ToolStripMenuItem CreateProfilesMenu(IReadOnlyList<ProfileStatus> profiles)
    {
        ToolStripMenuItem menu = TrayMenuItems.Menu("Profiles");
        if (profiles.Count == 0)
        {
            _ = menu.DropDownItems.Add(TrayMenuItems.Disabled("None"));
            return menu;
        }

        foreach (ProfileStatus profile in profiles)
        {
            _ = menu.DropDownItems.Add(CreateProfileMenu(profile));
        }

        return menu;
    }

    private static ToolStripMenuItem CreateProfileMenu(ProfileStatus profile)
    {
        ToolStripMenuItem menu = TrayMenuItems.Menu(profile.Title);
        _ = menu.DropDownItems.Add(TrayMenuItems.Item("ID", profile.Id));
        _ = menu.DropDownItems.Add(TrayMenuItems.Item("Steam app ID", TrayMenuItems.Number(profile.EffectiveSteamAppId)));
        _ = menu.DropDownItems.Add(TrayMenuItems.Item("Mouse output", TrayMenuItems.Output(profile.MouseOutput)));
        _ = menu.DropDownItems.Add(TrayMenuItems.Item("Controller output", TrayMenuItems.Output(profile.ControllerOutput)));
        _ = menu.DropDownItems.Add(TrayMenuItems.Item("Active", TrayMenuItems.Enabled(profile.Active)));
        _ = menu.DropDownItems.Add(TrayMenuItems.Item("Client", TrayMenuItems.Number(profile.ClientProcessId)));
        return menu;
    }

    private static ToolStripMenuItem CreateShortcutsMenu(BridgeServerStatus status)
    {
        ToolStripMenuItem menu = TrayMenuItems.Menu("Shortcuts");
        if (status.Shortcuts.Count == 0)
        {
            _ = menu.DropDownItems.Add(TrayMenuItems.Disabled("None"));
            return menu;
        }

        foreach (BridgeShortcutStatus shortcut in status.Shortcuts)
        {
            _ = menu.DropDownItems.Add(CreateShortcutMenu(shortcut));
        }

        return menu;
    }

    private static ToolStripMenuItem CreateShortcutMenu(BridgeShortcutStatus shortcut)
    {
        ToolStripMenuItem menu = TrayMenuItems.Menu(shortcut.Keys);
        _ = menu.DropDownItems.Add(TrayMenuItems.Item("Targets", string.Join(", ", shortcut.Targets)));
        _ = menu.DropDownItems.Add(TrayMenuItems.Item("Action", shortcut.Action));
        _ = menu.DropDownItems.Add(TrayMenuItems.Item("Pressed", TrayMenuItems.Enabled(shortcut.Pressed)));
        return menu;
    }

    private ToolStripMenuItem CreateStartupItem(bool isEnabled)
    {
        ToolStripMenuItem item = TrayMenuItems.Item("Start on startup", TrayMenuItems.Enabled(isEnabled));
        item.Click += (_, _) =>
        {
            TrayMenuItems.Run(TrayActions.ToggleStartup, onError);
            item.ShortcutKeyDisplayString = TrayMenuItems.Enabled(TrayActions.StartupEnabled);
        };
        return item;
    }
}
