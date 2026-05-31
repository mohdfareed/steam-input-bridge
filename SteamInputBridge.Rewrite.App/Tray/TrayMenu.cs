using System;
using System.Globalization;
using System.Windows.Forms;
using SteamInputBridge.Hosting;

namespace SteamInputBridge.App.Tray;

internal sealed class TrayMenu(
    Action openDesktopSteamInputConfig,
    Action exportSrmManifest,
    Action openSettings,
    Action openLogs,
    Func<bool> startupEnabled,
    Action toggleStartup,
    Action restart,
    Action<Guid> stopClient,
    Action exit,
    Action<Exception> onError)
{
    public ContextMenuStrip Menu { get; } = new()
    {
        RenderMode = ToolStripRenderMode.System,
    };

    // MARK: Publics
    // ========================================================================

    public void Rebuild(BridgeServerStatus status, bool isStartupEnabled)
    {
        Menu.SuspendLayout();
        try
        {
            Menu.Items.Clear();

            Add(Menu.Items, CreateDiagnosticsMenu(status));
            AddSeparator(Menu.Items);
            Add(Menu.Items, ActionItem("Open Steam Controller desktop config", openDesktopSteamInputConfig));
            Add(Menu.Items, ActionItem("Export SRM manifest", exportSrmManifest));
            AddSeparator(Menu.Items);
            Add(Menu.Items, ActionItem("Open settings", openSettings));
            Add(Menu.Items, ActionItem("Open logs", openLogs));
            AddSeparator(Menu.Items);
            Add(Menu.Items, CreateStartupItem(isStartupEnabled));
            Add(Menu.Items, ActionItem("Restart", restart));
            Add(Menu.Items, ActionItem("Exit", exit));
        }
        finally
        {
            Menu.ResumeLayout();
        }
    }

    // MARK: Implementation
    // ========================================================================

    private ToolStripMenuItem CreateDiagnosticsMenu(BridgeServerStatus status)
    {
        ToolStripMenuItem menu = MenuItem("Diagnostics");
        Add(menu.DropDownItems, CreateClientsMenu(status));
        return menu;
    }

    private ToolStripMenuItem CreateClientsMenu(BridgeServerStatus status)
    {
        string count = status.ClientsCount.ToString(CultureInfo.InvariantCulture);
        ToolStripMenuItem menu = MenuItem("Clients", count);
        if (status.Clients.Count == 0)
        {
            Add(menu.DropDownItems, DisabledItem("None"));
            return menu;
        }

        foreach (BridgeClientStatus client in status.Clients)
        {
            Add(menu.DropDownItems, CreateClientMenu(client));
        }

        return menu;
    }

    private ToolStripMenuItem CreateClientMenu(BridgeClientStatus client)
    {
        string processId = client.ProcessId.ToString(CultureInfo.InvariantCulture);
        ToolStripMenuItem menu = MenuItem(client.ProfileId, $"PID {processId}");
        Add(menu.DropDownItems, ActionItem("Stop client", () => stopClient(client.ConnectionId)));
        return menu;
    }

    private ToolStripMenuItem CreateStartupItem(bool isEnabled)
    {
        ToolStripMenuItem item = MenuItem("Start on startup", EnabledText(isEnabled));
        item.Click += (_, _) =>
        {
            RunAction(() =>
            {
                toggleStartup();
                item.ShortcutKeyDisplayString = EnabledText(startupEnabled());
            });
        };
        return item;
    }

    private ToolStripMenuItem ActionItem(string text, Action callback)
    {
        ToolStripMenuItem item = MenuItem(text);
        item.Click += (_, _) => RunAction(callback);
        return item;
    }

    private static ToolStripMenuItem DisabledItem(string text)
    {
        ToolStripMenuItem item = MenuItem(text);
        item.Enabled = false;
        return item;
    }

    private static ToolStripMenuItem MenuItem(string text, string? value = null)
    {
        ToolStripMenuItem item = new(text)
        {
            ShortcutKeyDisplayString = value ?? string.Empty,
        };
        item.DropDown.RenderMode = ToolStripRenderMode.System;
        return item;
    }

    private static void Add(ToolStripItemCollection items, ToolStripItem item)
    {
        _ = items.Add(item);
    }

    private static void AddSeparator(ToolStripItemCollection items)
    {
        Add(items, new ToolStripSeparator());
    }

    private void RunAction(Action callback)
    {
        try
        {
            callback();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            onError(exception);
        }
    }

    private static string EnabledText(bool enabled)
    {
        return enabled ? "Enabled" : "Disabled";
    }
}
