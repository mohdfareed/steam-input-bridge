using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Microphone;
using SteamInputBridge.Settings;
using SteamInputBridge.Shortcuts.Runtime;

namespace SteamInputBridge.Shortcuts;

/// <summary>Registers configured shortcuts and applies shortcut target behavior.</summary>
/// <param name="settings">Application settings.</param>
/// <param name="listener">Global shortcut listener.</param>
/// <param name="microphone">Microphone service controlled by microphone shortcuts.</param>
/// <param name="logger">Shortcut logger.</param>
public sealed class ShortcutService(
    SettingsService settings,
    GlobalShortcutListener listener,
    MicrophoneService microphone,
    ILogger<ShortcutService> logger) : IHostedService, IDisposable
{
    private readonly Lock _gate = new();
    private IReadOnlyDictionary<int, IReadOnlyList<ShortcutEntry>> _shortcuts = new Dictionary<int, IReadOnlyList<ShortcutEntry>>();
    private readonly List<MicrophoneHold> _microphoneHolds = [];
    private bool? _microphoneBaseline;
    private bool _disposed;

    // MARK: Lifecycle
    // ========================================================================

    internal event EventHandler<ShortcutEventArgs>? Shortcut;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        settings.Changed += OnSettingsChanged;
        Apply(settings.Current.Shortcuts);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        settings.Changed -= OnSettingsChanged;
        listener.Update([], static _ => { }, static _ => { });
        ClearMicrophoneHolds();
        return Task.CompletedTask;
    }

    /// <summary>Stops listening for shortcuts.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        settings.Changed -= OnSettingsChanged;
        listener.Dispose();
    }

    // MARK: Settings
    // ========================================================================

    private void OnSettingsChanged(object? sender, ApplicationSettingsChangedEventArgs args)
    {
        _ = sender;
        Apply(args.Settings.Shortcuts);
    }

    private void Apply(IEnumerable<ShortcutEntry> shortcuts)
    {
        ShortcutBindingSet bindings = ShortcutBindingSet.Create(shortcuts);
        lock (_gate)
        {
            _shortcuts = bindings.Shortcuts;
        }

        ClearMicrophoneHolds();

        try
        {
            listener.Update(bindings.Registrations, OnShortcutPressed, OnShortcutReleased);
            LogShortcutsRegistered(logger, bindings.Registrations.Count, null);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            LogShortcutRegistrationFailed(logger, exception.Message, null);
        }
    }

    // MARK: Shortcuts
    // ========================================================================

    private void OnShortcutPressed(int id)
    {
        if (!TryGetShortcuts(id, out IReadOnlyList<ShortcutEntry> shortcuts))
        {
            return;
        }

        foreach (ShortcutEntry shortcut in shortcuts)
        {
            ApplyPressed(id, shortcut);
        }
    }

    private void OnShortcutReleased(int id)
    {
        if (!TryGetShortcuts(id, out IReadOnlyList<ShortcutEntry> shortcuts))
        {
            return;
        }

        foreach (ShortcutEntry shortcut in shortcuts)
        {
            ApplyReleased(id, shortcut);
        }
    }

    private bool TryGetShortcuts(int id, out IReadOnlyList<ShortcutEntry> shortcuts)
    {
        lock (_gate)
        {
            if (_shortcuts.TryGetValue(id, out shortcuts!))
            {
                return true;
            }
        }

        shortcuts = [];
        return false;
    }

    private void ApplyPressed(int id, ShortcutEntry shortcut)
    {
        foreach (ShortcutTargetSetting target in shortcut.Targets)
        {
            Publish(id, shortcut, target, ShortcutPhase.Pressed);
            if (target.Target == ShortcutTarget.Microphone)
            {
                ApplyMicrophonePressed(id, shortcut.Action);
            }
        }
    }

    private void ApplyReleased(int id, ShortcutEntry shortcut)
    {
        foreach (ShortcutTargetSetting target in shortcut.Targets)
        {
            if (shortcut.Action is ShortcutValue.Enable or ShortcutValue.Disable)
            {
                Publish(id, shortcut, target, ShortcutPhase.Released);
            }

            if (target.Target == ShortcutTarget.Microphone)
            {
                ApplyMicrophoneReleased(id, shortcut.Action);
            }
        }
    }

    private void Publish(int id, ShortcutEntry shortcut, ShortcutTargetSetting target, ShortcutPhase phase)
    {
        Shortcut?.Invoke(this, new(id, shortcut.Keys, target, shortcut.Action, phase));
    }

    // MARK: Microphone
    // ========================================================================

    private void ApplyMicrophonePressed(int shortcutId, ShortcutValue action)
    {
        switch (action)
        {
            case ShortcutValue.Toggle:
                SetMicrophoneEnabled(!IsMicrophoneEnabled());
                break;
            case ShortcutValue.Enable:
                HoldMicrophone(shortcutId, enabled: true);
                break;
            case ShortcutValue.Disable:
                HoldMicrophone(shortcutId, enabled: false);
                break;
            default:
                break;
        }
    }

    private void ApplyMicrophoneReleased(int shortcutId, ShortcutValue action)
    {
        if (action is ShortcutValue.Enable or ShortcutValue.Disable)
        {
            ReleaseMicrophone(shortcutId);
        }
    }

    private void HoldMicrophone(int shortcutId, bool enabled)
    {
        lock (_gate)
        {
            _microphoneBaseline ??= IsMicrophoneEnabled();
            _ = _microphoneHolds.RemoveAll(hold => hold.ShortcutId == shortcutId);
            _microphoneHolds.Add(new(shortcutId, enabled));
        }

        SetCurrentMicrophoneHold();
    }

    private void ReleaseMicrophone(int shortcutId)
    {
        lock (_gate)
        {
            _ = _microphoneHolds.RemoveAll(hold => hold.ShortcutId == shortcutId);
        }

        SetCurrentMicrophoneHold();
    }

    private void SetCurrentMicrophoneHold()
    {
        bool enabled;
        lock (_gate)
        {
            if (_microphoneHolds.Count == 0)
            {
                enabled = _microphoneBaseline ?? IsMicrophoneEnabled();
                _microphoneBaseline = null;
            }
            else
            {
                enabled = _microphoneHolds[^1].Enabled;
            }
        }

        SetMicrophoneEnabled(enabled);
    }

    private void ClearMicrophoneHolds()
    {
        bool? baseline;
        lock (_gate)
        {
            baseline = _microphoneBaseline;
            _microphoneHolds.Clear();
            _microphoneBaseline = null;
        }

        if (baseline.HasValue)
        {
            SetMicrophoneEnabled(baseline.Value);
        }
    }

    private bool IsMicrophoneEnabled()
    {
        return microphone.GetStatus() is { Available: true, Muted: false };
    }

    private void SetMicrophoneEnabled(bool enabled)
    {
        try
        {
            microphone.SetEnabled(enabled);
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException)
        {
            LogMicrophoneShortcutFailed(logger, exception.Message, null);
        }
    }

    private readonly record struct MicrophoneHold(int ShortcutId, bool Enabled);

    // MARK: Logging
    // ========================================================================

    private static readonly Action<ILogger, int, Exception?> LogShortcutsRegistered =
        LoggerMessage.Define<int>(
            LogLevel.Information,
            new EventId(1, nameof(LogShortcutsRegistered)),
            "Registered {ShortcutCount} keyboard shortcut(s).");

    private static readonly Action<ILogger, string, Exception?> LogShortcutRegistrationFailed =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(2, nameof(LogShortcutRegistrationFailed)),
            "Keyboard shortcut registration failed: {Message}");

    private static readonly Action<ILogger, string, Exception?> LogMicrophoneShortcutFailed =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(3, nameof(LogMicrophoneShortcutFailed)),
            "Microphone shortcut failed: {Message}");
}
