using System;
using System.Collections.Generic;
using System.Windows.Forms;
using SteamInputBridge.Hosting;
using SteamInputBridge.Settings;

namespace SteamInputBridge.App.Tray.Menu;

internal sealed class TrayMenu(TrayActions actions, Action restart, Action exit, Action<Exception> onError)
{
    public ContextMenuStrip Menu { get; } = new()
    {
        RenderMode = ToolStripRenderMode.System,
    };

    // MARK: Publics
    // ========================================================================

    public void Rebuild(SteamInputBridgeSettings settings, BridgeServerStatus status, bool isStartupEnabled)
    {
        Menu.SuspendLayout();
        try
        {
            Menu.Items.Clear();
            _ = Menu.Items.Add(CreateConnectedClientsItem(status));
            _ = Menu.Items.Add(CreateProfilesMenu(settings, status));
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

    private static ToolStripMenuItem CreateConnectedClientsItem(BridgeServerStatus status)
    {
        ToolStripMenuItem item = TrayMenuItems.Item("Connected Clients", TrayMenuItems.Number(status.ClientsCount));
        item.Enabled = false;
        return item;
    }

    private static ToolStripMenuItem CreateProfilesMenu(SteamInputBridgeSettings settings, BridgeServerStatus status)
    {
        ToolStripMenuItem menu = TrayMenuItems.Menu("Profiles");
        Dictionary<string, BridgeClientStatus> clients = ConnectedClientsByProfile(status);
        if (settings.Games.Count == 0)
        {
            _ = menu.DropDownItems.Add(TrayMenuItems.Disabled("None"));
            return menu;
        }

        foreach ((string profileId, GameProfile profile) in settings.Games)
        {
            _ = clients.TryGetValue(profileId, out BridgeClientStatus? client);
            _ = menu.DropDownItems.Add(CreateProfileMenu(profileId, profile, client));
        }

        return menu;
    }

    private static ToolStripMenuItem CreateProfileMenu(string profileId, GameProfile profile, BridgeClientStatus? client)
    {
        ToolStripMenuItem menu = TrayMenuItems.Menu(profile.Title);
        _ = menu.DropDownItems.Add(TrayMenuItems.Item("ID", profileId));
        _ = menu.DropDownItems.Add(TrayMenuItems.Item("Steam app ID", TrayMenuItems.SteamAppId(profile, client)));
        _ = menu.DropDownItems.Add(TrayMenuItems.Item("Mouse output", TrayMenuItems.Output(profile.MouseOutput)));
        _ = menu.DropDownItems.Add(TrayMenuItems.Item("Controller output", TrayMenuItems.Output(profile.ControllerOutput)));
        _ = menu.DropDownItems.Add(TrayMenuItems.Item("Client", client is null ? "None" : TrayMenuItems.Number(client.ProcessId)));
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

    // MARK: Implementation
    // ========================================================================

    private static Dictionary<string, BridgeClientStatus> ConnectedClientsByProfile(BridgeServerStatus status)
    {
        Dictionary<string, BridgeClientStatus> clients = new(StringComparer.OrdinalIgnoreCase);
        foreach (BridgeClientStatus client in status.Clients)
        {
            clients[client.ProfileId] = client;
        }

        return clients;
    }
}
