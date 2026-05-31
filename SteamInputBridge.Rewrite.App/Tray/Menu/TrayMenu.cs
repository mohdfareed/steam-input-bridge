using System;
using System.Collections.Generic;
using System.Windows.Forms;
using SteamInputBridge.Hosting;
using SteamInputBridge.Profiles;

namespace SteamInputBridge.App.Tray.Menu;

internal sealed record TrayMenuState(
    IReadOnlyList<ProfileStatus> Profiles,
    string? LastActiveProfileId,
    BridgeServerStatus ServerStatus,
    bool StartupEnabled);

internal sealed class TrayMenu(TrayActions actions, Action restart, Action exit, Action<Exception> onError)
{
    private readonly ProfilesMenuSection _profiles = new();
    private readonly ShortcutsMenuSection _shortcuts = new();
    private ToolStripMenuItem? _startupItem;
    private TrayMenuState? _state;

    public ContextMenuStrip Menu { get; } = new()
    {
        RenderMode = ToolStripRenderMode.System,
    };

    // MARK: Publics
    // ========================================================================

    public void SetState(TrayMenuState state)
    {
        bool rebuild = _state is null ||
            ProfilesMenuSection.ShapeChanged(_state.Profiles, state.Profiles) ||
            ShortcutsMenuSection.ShapeChanged(_state.ServerStatus.Shortcuts, state.ServerStatus.Shortcuts);

        _state = state;
        if (rebuild)
        {
            RebuildWhenClosed();
            return;
        }

        UpdateLiveItems(state);
    }

    public void Rebuild()
    {
        if (_state is not null)
        {
            Rebuild(_state);
        }
    }

    // MARK: Build
    // ========================================================================

    private void RebuildWhenClosed()
    {
        if (_state is null)
        {
            return;
        }

        if (Menu.Visible)
        {
            UpdateLiveItems(_state);
            return;
        }

        Rebuild(_state);
    }

    private void Rebuild(TrayMenuState state)
    {
        Menu.SuspendLayout();
        try
        {
            _startupItem = null;
            Menu.Items.Clear();
            _ = Menu.Items.Add(_profiles.Build(state.Profiles, state.LastActiveProfileId));
            _ = Menu.Items.Add(_shortcuts.Build(state.ServerStatus.Shortcuts));
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
            _ = Menu.Items.Add(CreateStartupItem(state.StartupEnabled));
            _ = Menu.Items.Add(TrayMenuItems.ActionItem("Restart", restart, onError));
            _ = Menu.Items.Add(TrayMenuItems.ActionItem("Exit", exit, onError));
        }
        finally
        {
            Menu.ResumeLayout();
        }
    }

    // MARK: Updates
    // ========================================================================

    private void UpdateLiveItems(TrayMenuState state)
    {
        _profiles.Update(state.Profiles, state.LastActiveProfileId);
        _shortcuts.Update(state.ServerStatus.Shortcuts);
        if (_startupItem is not null)
        {
            TrayMenuItems.SetValue(_startupItem, TrayMenuItems.Enabled(state.StartupEnabled));
        }

        Menu.Invalidate();
    }

    // MARK: Static Items
    // ========================================================================

    private ToolStripMenuItem CreateStartupItem(bool isEnabled)
    {
        ToolStripMenuItem item = TrayMenuItems.Item("Start on startup", TrayMenuItems.Enabled(isEnabled));
        item.Click += (_, _) =>
        {
            TrayMenuItems.Run(TrayActions.ToggleStartup, onError);
            SetState(_state! with { StartupEnabled = TrayActions.StartupEnabled });
        };

        _startupItem = item;
        return item;
    }
}
