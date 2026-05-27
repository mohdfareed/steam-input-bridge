using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Forwarding.Controller.Routing;
using SteamInputBridge.Forwarding.Mouse;
using SteamInputBridge.Hosting.Server.Orchestration;
using SteamInputBridge.Runtime.Audio;
using SteamInputBridge.Settings;
using SteamInputBridge.Shortcuts;

namespace SteamInputBridge.Hosting.Server;

internal sealed class ServerShortcutService(
    ApplicationSettingsService settings,
    IKeyboardShortcutListener listener,
    ControllerBroker controllers,
    MouseBroker mouse,
    IMicrophoneControl microphone,
    ILogger<ServerShortcutService> logger) : IDisposable
{
    private readonly Lock _gate = new();
    private IReadOnlyDictionary<int, IReadOnlyList<ShortcutEntry>> _shortcuts =
        new Dictionary<int, IReadOnlyList<ShortcutEntry>>();
    private Dictionary<ShortcutTargetKey, ShortcutHold> _holds = [];
    private List<ShortcutColorSource> _actionColors = [];
    private bool _started;
    private bool _disposed;

    public event Action? StateChanged;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            return;
        }

        _started = true;
        settings.Changed += OnSettingsChanged;
        Apply(settings.Current.Shortcuts);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        settings.Changed -= OnSettingsChanged;
        ClearShortcutState();
        listener.Dispose();
        _disposed = true;
    }

    public OverlayStatus GetOverlayStatus()
    {
        string? actionColor;
        lock (_gate)
        {
            actionColor = _actionColors.Count == 0
                ? null
                : _actionColors[^1].Color;
        }

        return new OverlayStatus(microphone.GetStatus(), actionColor);
    }

    private void OnSettingsChanged(object? sender, ApplicationSettingsChangedEventArgs args)
    {
        _ = sender;
        Apply(args.Settings.Shortcuts);
    }

    private void Apply(IEnumerable<ShortcutEntry> entries)
    {
        KeyboardShortcutBindingSet bindings = KeyboardShortcutBindingSet.Create(
            entries,
            (entry, index, exception) =>
                HostingLog.ShortcutSkipped(logger, ShortcutName(entry, index), exception.Message));

        bool shortcutStateChanged;
        lock (_gate)
        {
            _shortcuts = bindings.Shortcuts;
            shortcutStateChanged = ClearShortcutStateLocked();
        }

        try
        {
            listener.Update(bindings.Registrations, OnShortcutPressed, OnShortcutReleased);
            HostingLog.ShortcutsRegistered(logger, bindings.Registrations.Count);
            if (shortcutStateChanged)
            {
                StateChanged?.Invoke();
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            HostingLog.ShortcutRegistrationFailed(logger, exception.Message);
        }
    }

    private void OnShortcutPressed(int id)
    {
        IReadOnlyList<ShortcutEntry>? shortcuts;
        lock (_gate)
        {
            _ = _shortcuts.TryGetValue(id, out shortcuts);
        }

        if (shortcuts is null)
        {
            return;
        }

        foreach (ShortcutEntry shortcut in shortcuts)
        {
            if (shortcut.Targets.Count == 0 || !shortcut.Value.HasValue)
            {
                continue;
            }

            ApplyShortcut(shortcut, id);
        }
    }

    private void ApplyShortcut(
        ShortcutEntry shortcut,
        int id)
    {
        ShortcutValue value = shortcut.Value.GetValueOrDefault();
        foreach (ShortcutTargetSpec target in shortcut.Targets)
        {
            switch (value)
            {
                case ShortcutValue.Enabled:
                    SetTarget(id, target, enabled: true);
                    CancelHold(new ShortcutTargetKey(id, target));
                    break;
                case ShortcutValue.Disabled:
                    SetTarget(id, target, enabled: false);
                    CancelHold(new ShortcutTargetKey(id, target));
                    break;
                case ShortcutValue.Toggle:
                    SetTarget(id, target, !GetTargetEnabled(id, target));
                    CancelHold(new ShortcutTargetKey(id, target));
                    break;
                case ShortcutValue.HoldEnabled:
                    StartHold(shortcut, id, target, enabledWhileHeld: true);
                    break;
                case ShortcutValue.HoldDisabled:
                    StartHold(shortcut, id, target, enabledWhileHeld: false);
                    break;
                default:
                    return;
            }

            if (value is ShortcutValue.Enabled or ShortcutValue.Disabled or ShortcutValue.Toggle)
            {
                LogShortcut(shortcut, id, target);
            }
        }
    }

    private void StartHold(
        ShortcutEntry shortcut,
        int id,
        ShortcutTargetSpec target,
        bool enabledWhileHeld)
    {
        ShortcutTargetKey key = new(id, target);
        lock (_gate)
        {
            _holds[key] = new ShortcutHold(EnabledOnRelease: !enabledWhileHeld);
        }

        SetTarget(id, target, enabledWhileHeld);
        LogShortcut(shortcut, id, target);
    }

    private void OnShortcutReleased(int id)
    {
        List<KeyValuePair<ShortcutTargetKey, ShortcutHold>> release = [];

        lock (_gate)
        {
            foreach (KeyValuePair<ShortcutTargetKey, ShortcutHold> hold in _holds)
            {
                if (hold.Key.ShortcutId == id)
                {
                    release.Add(hold);
                }
            }

            foreach (KeyValuePair<ShortcutTargetKey, ShortcutHold> hold in release)
            {
                _ = _holds.Remove(hold.Key);
            }
        }

        foreach (KeyValuePair<ShortcutTargetKey, ShortcutHold> hold in release)
        {
            SetTarget(id, hold.Key.Target, hold.Value.EnabledOnRelease);
        }
    }

    private void SetTarget(
        int shortcutId,
        ShortcutTargetSpec target,
        bool enabled)
    {
        if (target.Color is { Length: > 0 } color)
        {
            SetActionColor(shortcutId, color, enabled);
            return;
        }

        switch (target.Target)
        {
            case ShortcutTarget.Motion:
                controllers.SetPhysicalMotionEnabled(enabled);
                break;
            case ShortcutTarget.Pointer:
                mouse.SetPointerOutputEnabled(enabled);
                break;
            case ShortcutTarget.Mic:
                try
                {
                    microphone.SetEnabled(enabled);
                }
                catch (Exception exception) when (exception is COMException or InvalidOperationException)
                {
                    HostingLog.MicrophoneShortcutFailed(logger, exception.Message);
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(target), target, "Unknown shortcut target.");
        }

        StateChanged?.Invoke();
    }

    private bool GetTargetEnabled(
        int shortcutId,
        ShortcutTargetSpec target)
    {
        if (target.Color is { Length: > 0 } color)
        {
            lock (_gate)
            {
                return _actionColors.Contains(new ShortcutColorSource(shortcutId, color));
            }
        }

        return target.Target switch
        {
            ShortcutTarget.Motion => controllers.GetStatus().PhysicalMotionEnabled,
            ShortcutTarget.Pointer => mouse.GetStatus().PointerOutputEnabled,
            ShortcutTarget.Mic => microphone.GetStatus() is { Available: true, Muted: false },
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, "Unknown shortcut target."),
        };
    }

    private void SetActionColor(
        int shortcutId,
        string color,
        bool enabled)
    {
        ShortcutColorSource source = new(shortcutId, color);
        bool changed;
        lock (_gate)
        {
            changed = _actionColors.Remove(source);
            if (enabled)
            {
                _actionColors.Add(source);
                changed = true;
            }
        }

        if (changed)
        {
            StateChanged?.Invoke();
        }
    }

    private void CancelHold(ShortcutTargetKey target)
    {
        lock (_gate)
        {
            _ = _holds.Remove(target);
        }
    }

    private void ClearShortcutState()
    {
        bool changed;
        lock (_gate)
        {
            changed = ClearShortcutStateLocked();
        }

        if (changed)
        {
            StateChanged?.Invoke();
        }
    }

    private bool ClearShortcutStateLocked()
    {
        _holds = [];
        bool changed = _actionColors.Count != 0;
        _actionColors = [];
        return changed;
    }

    private void LogShortcut(ShortcutEntry shortcut, int id, ShortcutTargetSpec target)
    {
        if (!logger.IsEnabled(LogLevel.Information) ||
            !shortcut.Value.HasValue)
        {
            return;
        }

        string name = ShortcutName(shortcut, id - 1);
        HostingLog.ShortcutApplied(logger, name, target, shortcut.Value.Value);
    }

    private static string ShortcutName(ShortcutEntry entry, int index)
    {
        return string.IsNullOrWhiteSpace(entry.Name)
            ? $"#{index}"
            : entry.Name;
    }

    private readonly record struct ShortcutTargetKey(int ShortcutId, ShortcutTargetSpec Target);

    private readonly record struct ShortcutHold(bool EnabledOnRelease);

    private readonly record struct ShortcutColorSource(int ShortcutId, string Color);
}
