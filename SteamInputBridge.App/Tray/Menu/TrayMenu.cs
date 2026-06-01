using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;
using SteamInputBridge.Hosting;
using SteamInputBridge.Microphone;
using SteamInputBridge.Profiles;

namespace SteamInputBridge.App.Tray.Menu;

internal sealed record TrayMenuState(
    IReadOnlyList<ProfileStatus> Profiles,
    string? LastActiveProfileId,
    BridgeServerStatus ServerStatus,
    MicrophoneStatus Microphone,
    string? ActionColor,
    bool StartupEnabled);

internal sealed class TrayMenu(TrayActions actions, Action restart, Action exit, Action<Exception> onError)
{
    private readonly ProfilesMenuSection _profiles = new();
    private readonly ShortcutsMenuSection _shortcuts = new();
    private readonly DiagnosticsMenuSection _diagnostics = new();

    private ToolStripMenuItem? _startupItem;
    private ToolStripMenuItem? _teensy;
    private ToolStripMenuItem? _status;
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

    private void Rebuild(TrayMenuState state)
    {
        Menu.SuspendLayout();
        try
        {
            _startupItem = null;
            Menu.Items.Clear();

            // Profiles and shortcuts
            _ = Menu.Items.Add(_profiles.Build(
                state.Profiles,
                state.LastActiveProfileId,
                actions.OpenSteamInputConfigAsync,
                onError,
                connectionId => _ = TrayMenuItems.RunAsync(() => actions.StopClientAsync(connectionId), onError)));
            _ = Menu.Items.Add(_shortcuts.Build(state.ServerStatus.Shortcuts));

            // Teensy
            _ = Menu.Items.Add(CreateTeensyItem(
                state.ServerStatus.Teensy,
                actions.UploadTeensyFirmwareAsync,
                onError));

            // Diagnostics
            _ = Menu.Items.Add(_diagnostics.Build(
                state.ServerStatus,
                state.Microphone,
                state.ActionColor));
            _ = Menu.Items.Add(new ToolStripSeparator());

            // Steam
            _ = Menu.Items.Add(TrayMenuItems.ActionItem("Export SRM manifest", actions.ExportSrmManifest, onError));
            _ = Menu.Items.Add(TrayMenuItems.ActionItem(
                "Open Steam Input desktop config",
                actions.OpenDesktopSteamInputConfigAsync,
                onError));

            // Settings
            _ = Menu.Items.Add(TrayMenuItems.ActionItem("Open settings", actions.OpenSettings, onError));
            _ = Menu.Items.Add(TrayMenuItems.ActionItem("Open logs", actions.OpenLogs, onError));
            _ = Menu.Items.Add(new ToolStripSeparator());

            // Lifecycle
            _ = Menu.Items.Add(CreateStartupItem(state.StartupEnabled));
            _ = Menu.Items.Add(TrayMenuItems.ActionItem("Restart", restart, onError));
            _ = Menu.Items.Add(TrayMenuItems.ActionItem("Exit", exit, onError));
        }
        finally
        {
            Menu.ResumeLayout();
        }
    }

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

    // MARK: Updates
    // ========================================================================

    private void UpdateLiveItems(TrayMenuState state)
    {
        _profiles.Update(state.Profiles, state.LastActiveProfileId);
        _shortcuts.Update(state.ServerStatus.Shortcuts);
        _diagnostics.Update(state.ServerStatus, state.Microphone, state.ActionColor);

        if (_startupItem is not null)
        {
            TrayMenuItems.SetCheckMark(_startupItem, state.StartupEnabled);
        }
        if (_teensy is not null)
        {
            TrayMenuItems.SetCheckMark(_teensy, state.ServerStatus.Teensy.Connected);
        }
        if (_status is not null)
        {
            TrayMenuItems.SetValue(_status, FormatStatus(state.ServerStatus.Teensy));
            TrayMenuItems.SetCheckMark(_status, state.ServerStatus.Teensy.Connected);
        }

        Menu.Invalidate();
    }

    // MARK: Static Items
    // ========================================================================

    private ToolStripMenuItem CreateStartupItem(bool isEnabled)
    {
        ToolStripMenuItem item = TrayMenuItems.Item("Start on startup");
        TrayMenuItems.SetCheckMark(item, isEnabled);
        item.Click += (_, _) =>
        {
            TrayMenuItems.Run(TrayActions.ToggleStartup, onError);
            SetState(_state! with { StartupEnabled = TrayActions.StartupEnabled });
        };

        _startupItem = item;
        return item;
    }

    private ToolStripMenuItem CreateTeensyItem(BridgeTeensyStatus status, Func<Task> uploadFirmware, Action<Exception> onError)
    {
        _teensy = TrayMenuItems.Menu("Teensy");
        _status = TrayMenuItems.Item("Status", FormatStatus(status));
        TrayMenuItems.SetCheckMark(_teensy, status.Connected);
        TrayMenuItems.SetCheckMark(_status, status.Connected);

        _ = _teensy.DropDownItems.Add(_status);
        _ = _teensy.DropDownItems.Add(TrayMenuItems.ActionItem("Upload firmware", uploadFirmware, onError));
        return _teensy;
    }

    private static string FormatStatus(BridgeTeensyStatus status)
    {
        return status.Connected && !string.IsNullOrWhiteSpace(status.ConnectedPort)
            ? $"Connected ({status.ConnectedPort})"
            : status.State;
    }
}
