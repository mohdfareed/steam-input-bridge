using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Forwarding.Controller;
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
    private Dictionary<int, IReadOnlyList<ShortcutEntry>> _shortcuts = [];
    private Dictionary<int, KeyboardShortcutCombination> _combinations = [];
    private Dictionary<ShortcutTarget, CancellationTokenSource> _holds = [];
    private bool _started;
    private bool _disposed;

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

    private void Apply(Collection<ShortcutEntry> entries)
    {
        Dictionary<int, List<ShortcutEntry>> shortcuts = [];
        List<KeyboardShortcutRegistration> registrations = [];
        Dictionary<KeyboardShortcutCombination, int> idsByCombination = [];
        for (int i = 0; i < entries.Count; i++)
        {
            ShortcutEntry entry = entries[i];
            KeyboardShortcutCombination combination;
            try
            {
                combination = KeyboardShortcutParser.Parse(entry.Keys);
            }
            catch (FormatException exception)
            {
                HostingLog.ShortcutSkipped(logger, ShortcutName(entry, i), exception.Message);
                continue;
            }

            if (!idsByCombination.TryGetValue(combination, out int id))
            {
                id = idsByCombination.Count + 1;
                idsByCombination[combination] = id;
                shortcuts[id] = [];
                registrations.Add(new KeyboardShortcutRegistration(id, combination));
            }

            shortcuts[id].Add(entry);
        }

        lock (_gate)
        {
            _shortcuts = shortcuts.ToDictionary(
                static item => item.Key,
                static item => (IReadOnlyList<ShortcutEntry>)item.Value);
            _combinations = idsByCombination.ToDictionary(
                static item => item.Value,
                static item => item.Key);
            CancelHolds();
        }

        try
        {
            listener.Update(registrations, OnShortcutPressed);
            HostingLog.ShortcutsRegistered(logger, registrations.Count);
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
            if (!shortcut.Target.HasValue || !shortcut.Value.HasValue)
            {
                continue;
            }

            KeyboardShortcutCombination combination;
            lock (_gate)
            {
                _ = _combinations.TryGetValue(id, out combination);
            }

            ApplyShortcut(shortcut, id, combination);
        }
    }

    private void ApplyShortcut(
        ShortcutEntry shortcut,
        int id,
        KeyboardShortcutCombination combination)
    {
        ShortcutTarget target = shortcut.Target.GetValueOrDefault();
        ShortcutValue value = shortcut.Value.GetValueOrDefault();
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
                StartHold(shortcut, id, combination, enabledWhileHeld: true);
                break;
            case ShortcutValue.HoldDisabled:
                StartHold(shortcut, id, combination, enabledWhileHeld: false);
                break;
            default:
                return;
        }

        if (value is ShortcutValue.Enabled or ShortcutValue.Disabled or ShortcutValue.Toggle)
        {
            LogShortcut(shortcut, id);
        }
    }

    private void StartHold(
        ShortcutEntry shortcut,
        int id,
        KeyboardShortcutCombination combination,
        bool enabledWhileHeld)
    {
        ShortcutTarget target = shortcut.Target.GetValueOrDefault();
        CancellationTokenSource hold = new();
        CancellationTokenSource? previous;
        lock (_gate)
        {
            _ = _holds.TryGetValue(target, out previous);
            _holds[target] = hold;
        }

        previous?.Cancel();
        previous?.Dispose();

        SetTarget(target, enabledWhileHeld);
        LogShortcut(shortcut, id);
        _ = ReleaseWhenShortcutUpAsync(
            target,
            combination,
            enabledOnRelease: !enabledWhileHeld,
            hold);
    }

    private async Task ReleaseWhenShortcutUpAsync(
        ShortcutTarget target,
        KeyboardShortcutCombination combination,
        bool enabledOnRelease,
        CancellationTokenSource hold)
    {
        try
        {
            while (!hold.IsCancellationRequested && IsCombinationDown(combination))
            {
                await Task.Delay(25, hold.Token).ConfigureAwait(false);
            }

            if (!hold.IsCancellationRequested)
            {
                SetTarget(target, enabledOnRelease);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            lock (_gate)
            {
                if (_holds.TryGetValue(target, out CancellationTokenSource? current) &&
                    ReferenceEquals(current, hold))
                {
                    _ = _holds.Remove(target);
                }
            }

            hold.Dispose();
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
        CancellationTokenSource? hold;
        lock (_gate)
        {
            if (!_holds.Remove(target, out hold))
            {
                return;
            }
        }

        hold.Cancel();
        hold.Dispose();
    }

    private void CancelHolds()
    {
        foreach (CancellationTokenSource hold in _holds.Values)
        {
            hold.Cancel();
            hold.Dispose();
        }

        _holds = [];
    }

    private void LogShortcut(ShortcutEntry shortcut, int id)
    {
        if (!logger.IsEnabled(LogLevel.Information) ||
            !shortcut.Target.HasValue ||
            !shortcut.Value.HasValue)
        {
            return;
        }

        string name = ShortcutName(shortcut, id - 1);
        HostingLog.ShortcutApplied(logger, name, shortcut.Target.Value, shortcut.Value.Value);
    }

    private static bool IsCombinationDown(KeyboardShortcutCombination combination)
    {
        return IsKeyDown(combination.VirtualKey) &&
            HasModifierState(combination.Modifiers, KeyboardShortcutModifiers.Control, 0x11) &&
            HasModifierState(combination.Modifiers, KeyboardShortcutModifiers.Alt, 0x12) &&
            HasModifierState(combination.Modifiers, KeyboardShortcutModifiers.Shift, 0x10) &&
            HasModifierState(combination.Modifiers, KeyboardShortcutModifiers.Windows, 0x5B, 0x5C);
    }

    private static bool HasModifierState(
        KeyboardShortcutModifiers actual,
        KeyboardShortcutModifiers expected,
        ushort virtualKey,
        ushort? alternateVirtualKey = null)
    {
        return (actual & expected) == 0 ||
            IsKeyDown(virtualKey) ||
            (alternateVirtualKey.HasValue && IsKeyDown(alternateVirtualKey.Value));
    }

    private static bool IsKeyDown(ushort virtualKey)
    {
        return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }

    private static string ShortcutName(ShortcutEntry entry, int index)
    {
        return string.IsNullOrWhiteSpace(entry.Name)
            ? $"#{index}"
            : entry.Name;
    }

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern short GetAsyncKeyState(int vKey);
}
