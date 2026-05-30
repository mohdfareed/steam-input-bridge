using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using SteamInputBridge.Hosting.Server.Orchestration;
using SteamInputBridge.Runtime;

namespace SteamInputBridge.App.Tray.Menu;

internal sealed record TrayActivitySnapshot(ClientStatus ActiveClient, ServerSteamInputStatus SteamInput);

internal sealed partial class AppMenu(
    string settingsPath,
    string? logPath,
    Action exportSrm,
    Action restart,
    Action openDesktopSteamInputConfig,
    Action<uint> openSteamInputConfig,
    Action<Guid> stopClient,
    Action exit,
    Action<Exception> onError)
{
    private const string ShortcutColorStatus = "shortcuts.color";
    private const string ShortcutMicStatus = "shortcuts.mic";
    private const string SteamForcedStatus = "steam.forced";
    private const string SteamAppIdStatus = "steam.appId";
    private const string SteamProfileStatus = "steam.profile";
    private const string InputPhysicalControllersStatus = "inputs.physicalControllers";
    private const string InputClientStreamsStatus = "inputs.clientStreams";
    private const string InputRawMouseStatus = "inputs.rawMouse";
    private const string OutputControllerEmulationStatus = "outputs.controllerEmulation";
    private const string OutputMotionStatus = "outputs.motion";
    private const string OutputMouseEmulationStatus = "outputs.mouseEmulation";
    private const string OutputPointerStatus = "outputs.pointer";
    private const string OutputVirtualMouseStatus = "outputs.virtualMouse";
    private const string OutputControllerSlotsStatus = "outputs.controllerSlots";
    private const string ClientMenuStatusPrefix = "clients.menu.";
    private const string ClientStateStatusPrefix = "clients.state.";

    private readonly Dictionary<string, ToolStripMenuItem> _statusItems = [];

    public ContextMenuStrip Menu { get; } = CreateMenu();

    public void Rebuild(ServerStatus? status, string? serverError, TrayActivitySnapshot? lastActivity)
    {
        Menu.SuspendLayout();
        try
        {
            _statusItems.Clear();
            Menu.Items.Clear();

            if (!string.IsNullOrWhiteSpace(serverError))
            {
                Add(Menu.Items, DisabledItem(AppText.Header(serverError)));
                AddSeparator(Menu.Items);
            }

            Add(Menu.Items, CreateClientsMenu(status, lastActivity));
            Add(Menu.Items, CreateDiagnosticsMenu(status, lastActivity));
            AddSeparator(Menu.Items);
            Add(Menu.Items, ActionItem("Open Steam Controller desktop config", openDesktopSteamInputConfig));
            Add(Menu.Items, ActionItem("Export SRM manifest", exportSrm));
            AddSeparator(Menu.Items);
            Add(Menu.Items, ActionItem("Open settings", () => OpenFile(settingsPath)));
            Add(Menu.Items, CreateOpenLogsItem());
            AddSeparator(Menu.Items);
            Add(Menu.Items, CreateStartupItem());
            Add(Menu.Items, ActionItem("Restart", restart));
            Add(Menu.Items, ActionItem("Exit", exit));
        }
        finally
        {
            Menu.ResumeLayout();
        }
    }

    public void RefreshVisibleStatus(ServerStatus? status, TrayActivitySnapshot? lastActivity)
    {
        if (status is null)
        {
            return;
        }

        SetValue(ShortcutColorStatus, status.Overlay.ActionColor ?? AppText.None);
        SetValue(ShortcutMicStatus, AppText.MicrophoneMuted(status.Overlay.Microphone));
        foreach (ShortcutStatus shortcut in status.Shortcuts.Shortcuts)
        {
            SetValue(ShortcutStatusKey(shortcut.ShortcutId), AppText.Held(shortcut.Held));
        }

        SetValue(InputPhysicalControllersStatus, AppText.ControllerInput(status.Inputs.Controller));
        SetValue(InputClientStreamsStatus, AppText.Count(CountClientControllers(status.ControllerPipes)));
        SetValue(InputRawMouseStatus, AppText.MouseInput(status.Inputs.Mouse));

        SetValue(OutputControllerEmulationStatus, AppText.Enabled(HasConnectedControllerOutput(status.Forwarding)));
        SetValue(OutputMotionStatus, AppText.Enabled(status.Forwarding.PhysicalMotionEnabled));
        SetValue(OutputMouseEmulationStatus, AppText.Enabled(HasConnectedMouseOutput(status.MouseForwarding)));
        SetValue(OutputPointerStatus, AppText.Enabled(status.MouseForwarding.PointerOutputEnabled));
        SetValue(OutputVirtualMouseStatus, AppText.FormatMouseOutput(status.MouseForwarding));
        SetValue(OutputControllerSlotsStatus, ControllerSlots(status.Forwarding.Slots));

        foreach (ClientStatus client in status.Runtime.Clients)
        {
            string value = AppText.Active(IsDisplayActive(client, lastActivity));
            SetValue(ClientMenuStatusKey(client.ClientId), value);
            SetValue(ClientStateStatusKey(client.ClientId), value);
        }

        ServerSteamInputStatus steamInput = lastActivity?.SteamInput ?? status.SteamInput;
        SetValue(SteamForcedStatus, AppText.Enabled(steamInput.Forced));
        SetValue(SteamAppIdStatus, AppText.AppId(steamInput.AppId));
        if (lastActivity is not null)
        {
            SetValue(SteamProfileStatus, lastActivity.ActiveClient.ProfileId);
        }
    }

    private ToolStripMenuItem CreateClientsMenu(ServerStatus? status, TrayActivitySnapshot? lastActivity)
    {
        ToolStripMenuItem menu = MenuItem("Clients");
        if (status is null || status.Runtime.Clients.Count == 0)
        {
            Add(menu.DropDownItems, DisabledItem(AppText.None));
            return menu;
        }

        foreach (ClientStatus client in status.Runtime.Clients)
        {
            Add(menu.DropDownItems, CreateClientMenu(client, lastActivity));
        }

        return menu;
    }

    private ToolStripMenuItem CreateClientMenu(ClientStatus client, TrayActivitySnapshot? lastActivity)
    {
        string value = AppText.Active(IsDisplayActive(client, lastActivity));
        ToolStripMenuItem menu = MenuItem(client.ProfileId, value, ClientMenuStatusKey(client.ClientId));
        Add(menu.DropDownItems, StatusItem("State", value, ClientStateStatusKey(client.ClientId)));
        Add(menu.DropDownItems, StatusItem("PID", client.ClientProcessId.ToString(CultureInfo.InvariantCulture)));
        Add(menu.DropDownItems, StatusItem("Steam App", AppText.AppId(client.SteamAppId)));
        Add(menu.DropDownItems, StatusItem("Receivers", AppText.Processes(client.ObservedProcesses)));
        AddSeparator(menu.DropDownItems);

        if (client.SteamAppId is uint appId)
        {
            Add(menu.DropDownItems, ActionItem("Open Steam Input config", () => openSteamInputConfig(appId)));
        }
        else
        {
            Add(menu.DropDownItems, DisabledItem("Open Steam Input config (no app id)"));
        }

        Add(menu.DropDownItems, ActionItem("Stop client", () => stopClient(client.ClientId)));
        return menu;
    }

    private ToolStripMenuItem CreateStartupItem()
    {
        ToolStripMenuItem item = Item(
            "Start on startup",
            AppText.Enabled(StartupRegistration.IsEnabled()),
            enabled: true);
        item.Click += (_, _) => RunAction(() =>
        {
            bool enabled = !StartupRegistration.IsEnabled();
            StartupRegistration.SetEnabled(enabled);
            item.ShortcutKeyDisplayString = AppText.Enabled(enabled);
        });
        return item;
    }

    private ToolStripMenuItem CreateOpenLogsItem()
    {
        return string.IsNullOrWhiteSpace(logPath)
            ? DisabledItem("Open logs (not configured)")
            : ActionItem("Open logs", () => OpenLogFile(logPath));
    }

    private ToolStripMenuItem ActionItem(string text, Action callback)
    {
        ToolStripMenuItem item = Item(text, value: null, enabled: true);
        item.Click += (_, _) => RunAction(callback);
        return item;
    }

    private static ToolStripMenuItem DisabledItem(string text)
    {
        return Item(text, value: null, enabled: false);
    }

    private ToolStripMenuItem StatusItem(string label, string value, string? key = null)
    {
        ToolStripMenuItem item = Item(label, value, enabled: false);
        if (key is not null)
        {
            _statusItems[key] = item;
        }

        return item;
    }

    private static ToolStripMenuItem MenuItem(string text, string? value = null, string? key = null)
    {
        ToolStripMenuItem item = Item(text, value, enabled: true);
        item.Tag = key;
        item.DropDown.RenderMode = ToolStripRenderMode.System;
        if (item.DropDown is ToolStripDropDownMenu menu)
        {
            menu.ShowCheckMargin = false;
            menu.ShowImageMargin = false;
        }

        return item;
    }

    private static ToolStripMenuItem Item(string text, string? value, bool enabled)
    {
        return new ToolStripMenuItem(text)
        {
            Enabled = enabled,
            ShortcutKeyDisplayString = value ?? string.Empty,
        };
    }

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "ToolStripItem ownership is transferred to the collection and disposed with the owning menu.")]
    private static void Add(ToolStripItemCollection items, ToolStripItem item)
    {
        _ = items.Add(item);
    }

    private static void AddSeparator(ToolStripItemCollection items)
    {
        Add(items, new ToolStripSeparator());
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Tray action failures should be reported without crashing the background app.")]
    private void RunAction(Action callback)
    {
        try
        {
            callback();
        }
        catch (Exception exception)
        {
            onError(exception);
        }
    }

    private void SetValue(string key, string value)
    {
        if (_statusItems.TryGetValue(key, out ToolStripMenuItem? item))
        {
            item.ShortcutKeyDisplayString = value;
        }
    }

    private static bool IsDisplayActive(ClientStatus client, TrayActivitySnapshot? lastActivity)
    {
        return client.IsActive || lastActivity?.ActiveClient.ClientId == client.ClientId;
    }

    private static string ShortcutStatusKey(int shortcutId)
    {
        return "shortcuts." + shortcutId.ToString(CultureInfo.InvariantCulture);
    }

    private static string ClientMenuStatusKey(Guid clientId)
    {
        return ClientMenuStatusPrefix + clientId.ToString("N");
    }

    private static string ClientStateStatusKey(Guid clientId)
    {
        return ClientStateStatusPrefix + clientId.ToString("N");
    }

    private static ContextMenuStrip CreateMenu()
    {
        return new ContextMenuStrip
        {
            RenderMode = ToolStripRenderMode.System,
            ShowCheckMargin = false,
            ShowImageMargin = false,
        };
    }

    private static void OpenFile(string path)
    {
        string fullPath = Path.GetFullPath(path);
        System.Diagnostics.ProcessStartInfo start = new()
        {
            FileName = fullPath,
            UseShellExecute = true,
        };
        _ = System.Diagnostics.Process.Start(start) ??
            throw new InvalidOperationException($"Could not open {fullPath}.");
    }

    private static void OpenLogFile(string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            _ = Directory.CreateDirectory(directory);
        }

        using (File.Open(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite))
        {
        }

        OpenFile(path);
    }
}
