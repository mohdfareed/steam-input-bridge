using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Hosting;
using SteamInputBridge.Settings;
using SteamInputBridge.Shortcuts.Runtime;

namespace SteamInputBridge.Shortcuts;

/// <summary>Registers configured shortcuts and applies shortcut target behavior.</summary>
public sealed class ShortcutService : IHostedService, IDisposable
{
    private readonly SettingsService _settings;
    private readonly IGlobalShortcutListener _listener;
    private readonly ILogger<ShortcutService> _logger;
    private readonly Lock _gate = new();
    private IReadOnlyDictionary<int, IReadOnlyList<ShortcutEntry>> _shortcuts = new Dictionary<int, IReadOnlyList<ShortcutEntry>>();
    private readonly HashSet<int> _pressedShortcuts = [];
    private bool _disposed;

    /// <summary>Creates the shortcut service.</summary>
    public ShortcutService(
        SettingsService settings,
        GlobalShortcutListener listener,
        ILogger<ShortcutService> logger)
        : this(settings, (IGlobalShortcutListener)(listener ?? throw new ArgumentNullException(nameof(listener))), logger)
    {
    }

    internal ShortcutService(
        SettingsService settings,
        IGlobalShortcutListener listener,
        ILogger<ShortcutService> logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(listener);
        ArgumentNullException.ThrowIfNull(logger);

        _settings = settings;
        _listener = listener;
        _logger = logger;
    }

    // MARK: Lifecycle
    // ========================================================================

    internal event EventHandler<ShortcutEventArgs>? Shortcut;

    /// <summary>Raised after shortcut status changes.</summary>
    public event EventHandler? StatusChanged;

    /// <summary>Current shortcut status snapshot.</summary>
    public IReadOnlyList<BridgeShortcutStatus> Status => GetStatus();

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        _settings.Changed += OnSettingsChanged;
        Apply(_settings.Current.Shortcuts);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        _settings.Changed -= OnSettingsChanged;
        _listener.Update([], static _ => { }, static _ => { });
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
        _settings.Changed -= OnSettingsChanged;
        _listener.Dispose();
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
            _pressedShortcuts.Clear();
        }

        StatusChanged?.Invoke(this, EventArgs.Empty);

        try
        {
            _listener.Update(bindings.Registrations, OnShortcutPressed, OnShortcutReleased);
            LogShortcutsRegistered(_logger, bindings.Registrations.Count, null);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            LogShortcutRegistrationFailed(_logger, exception.Message, null);
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

        bool changed;
        lock (_gate)
        {
            changed = _pressedShortcuts.Add(id);
        }

        foreach (ShortcutEntry shortcut in shortcuts)
        {
            ApplyPressed(id, shortcut);
        }

        if (changed)
        {
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnShortcutReleased(int id)
    {
        if (!TryGetShortcuts(id, out IReadOnlyList<ShortcutEntry> shortcuts))
        {
            return;
        }

        bool changed;
        lock (_gate)
        {
            changed = _pressedShortcuts.Remove(id);
        }

        foreach (ShortcutEntry shortcut in shortcuts)
        {
            ApplyReleased(id, shortcut);
        }

        if (changed)
        {
            StatusChanged?.Invoke(this, EventArgs.Empty);
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
        }
    }

    private void Publish(int id, ShortcutEntry shortcut, ShortcutTargetSetting target, ShortcutPhase phase)
    {
        Shortcut?.Invoke(this, new(id, shortcut.Keys, target, shortcut.Action, phase));
    }

    // MARK: Status
    // ========================================================================

    private List<BridgeShortcutStatus> GetStatus()
    {
        lock (_gate)
        {
            List<BridgeShortcutStatus> status = [];
            foreach ((int shortcutId, IReadOnlyList<ShortcutEntry> entries) in _shortcuts)
            {
                foreach (ShortcutEntry entry in entries)
                {
                    status.Add(new(
                        entry.Keys,
                        ShortcutTargets(entry),
                        entry.Action.ToString(),
                        _pressedShortcuts.Contains(shortcutId)));
                }
            }

            return status;
        }
    }

    private static List<string> ShortcutTargets(ShortcutEntry entry)
    {
        List<string> targets = new(entry.Targets.Count);
        foreach (ShortcutTargetSetting target in entry.Targets)
        {
            targets.Add(target.ToString());
        }

        return targets;
    }

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
}
