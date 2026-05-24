using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Forwarding.Controller.Routing;
using SteamInputBridge.Forwarding.Mouse;
using SteamInputBridge.Settings;
using SteamInputBridge.Shortcuts;

namespace SteamInputBridge.Hosting.Server;

internal sealed class ServerShortcutService(
    ApplicationSettingsService settings,
    IKeyboardShortcutListener listener,
    ControllerBroker controllers,
    MouseBroker mouse,
    ILogger<ServerShortcutService> logger) : IDisposable
{
    private readonly Lock _gate = new();
    private IReadOnlyDictionary<int, IReadOnlyList<ShortcutEntry>> _shortcuts =
        new Dictionary<int, IReadOnlyList<ShortcutEntry>>();
    private Dictionary<ShortcutTarget, ShortcutHold> _holds = [];
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
        CancelHolds();
        listener.Dispose();
        _disposed = true;
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

        lock (_gate)
        {
            _shortcuts = bindings.Shortcuts;
            CancelHolds();
        }

        try
        {
            listener.Update(bindings.Registrations, OnShortcutPressed, OnShortcutReleased);
            HostingLog.ShortcutsRegistered(logger, bindings.Registrations.Count);
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
        foreach (ShortcutTarget target in shortcut.Targets)
        {
            switch (value)
            {
                case ShortcutValue.Enabled:
                    SetTarget(target, enabled: true);
                    CancelHold(target);
                    break;
                case ShortcutValue.Disabled:
                    SetTarget(target, enabled: false);
                    CancelHold(target);
                    break;
                case ShortcutValue.Toggle:
                    SetTarget(target, !GetTargetEnabled(target));
                    CancelHold(target);
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
        ShortcutTarget target,
        bool enabledWhileHeld)
    {
        lock (_gate)
        {
            _holds[target] = new ShortcutHold(id, EnabledOnRelease: !enabledWhileHeld);
        }

        SetTarget(target, enabledWhileHeld);
        LogShortcut(shortcut, id, target);
    }

    private void OnShortcutReleased(int id)
    {
        List<KeyValuePair<ShortcutTarget, ShortcutHold>> release = [];

        lock (_gate)
        {
            foreach (KeyValuePair<ShortcutTarget, ShortcutHold> hold in _holds)
            {
                if (hold.Value.ShortcutId == id)
                {
                    release.Add(hold);
                }
            }

            foreach (KeyValuePair<ShortcutTarget, ShortcutHold> hold in release)
            {
                _ = _holds.Remove(hold.Key);
            }
        }

        foreach (KeyValuePair<ShortcutTarget, ShortcutHold> hold in release)
        {
            SetTarget(hold.Key, hold.Value.EnabledOnRelease);
        }
    }

    private void SetTarget(ShortcutTarget target, bool enabled)
    {
        switch (target)
        {
            case ShortcutTarget.Motion:
                controllers.SetPhysicalMotionEnabled(enabled);
                break;
            case ShortcutTarget.Pointer:
                mouse.SetPointerOutputEnabled(enabled);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(target), target, "Unknown shortcut target.");
        }

        StateChanged?.Invoke();
    }

    private bool GetTargetEnabled(ShortcutTarget target)
    {
        return target switch
        {
            ShortcutTarget.Motion => controllers.GetStatus().PhysicalMotionEnabled,
            ShortcutTarget.Pointer => mouse.GetStatus().PointerOutputEnabled,
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, "Unknown shortcut target."),
        };
    }

    private void CancelHold(ShortcutTarget target)
    {
        lock (_gate)
        {
            _ = _holds.Remove(target);
        }
    }

    private void CancelHolds()
    {
        _holds = [];
    }

    private void LogShortcut(ShortcutEntry shortcut, int id, ShortcutTarget target)
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

    private readonly record struct ShortcutHold(int ShortcutId, bool EnabledOnRelease);
}
