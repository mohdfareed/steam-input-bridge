using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Hosting;
using SteamInputBridge.Profiles;
using SteamInputBridge.Settings;
using SteamInputBridge.Shortcuts.Runtime;

namespace SteamInputBridge.Shortcuts;

/// <summary>Registers configured shortcuts and applies shortcut target behavior.</summary>
public sealed class ShortcutService : IHostedService, IDisposable, IShortcutSource
{
    private readonly SettingsService _settings;
    private readonly ActiveProfileService _profiles;
    private readonly IGlobalShortcutListener _listener;
    private readonly ILogger<ShortcutService> _logger;
    private readonly Lock _gate = new();
    private IReadOnlyDictionary<int, IReadOnlyList<ShortcutEntry>> _shortcuts = new Dictionary<int, IReadOnlyList<ShortcutEntry>>();
    private readonly HashSet<int> _pressedShortcuts = [];
    private bool _disposed;

    /// <summary>Creates the shortcut service.</summary>
    public ShortcutService(
        SettingsService settings,
        ActiveProfileService profiles,
        GlobalShortcutListener listener,
        ILogger<ShortcutService> logger)
        : this(settings, profiles, (IGlobalShortcutListener)listener, logger)
    {
    }

    internal ShortcutService(
        SettingsService settings,
        ActiveProfileService profiles,
        IGlobalShortcutListener listener,
        ILogger<ShortcutService> logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(listener);
        ArgumentNullException.ThrowIfNull(logger);

        _settings = settings;
        _profiles = profiles;
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

    event EventHandler<ShortcutEventArgs>? IShortcutSource.Shortcut
    {
        add => Shortcut += value;
        remove => Shortcut -= value;
    }

    IReadOnlyList<BridgeShortcutStatus> IShortcutSource.Status => Status;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        _settings.Changed += OnSettingsChanged;
        _profiles.ActiveProfileChanged += OnActiveProfileChanged;
        ApplySettings(_settings.Current);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        _settings.Changed -= OnSettingsChanged;
        _profiles.ActiveProfileChanged -= OnActiveProfileChanged;
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
        _profiles.ActiveProfileChanged -= OnActiveProfileChanged;
        _listener.Dispose();
    }

    // MARK: Settings
    // ========================================================================

    private void OnSettingsChanged(object? sender, ApplicationSettingsChangedEventArgs args)
    {
        _ = sender;
        ApplySettings(args.Settings);
    }

    private void OnActiveProfileChanged(object? sender, ActiveProfileChangedEventArgs args)
    {
        _ = sender;
        _ = args;
        ApplySettings(_settings.Current);
    }

    private void ApplySettings(SteamInputBridgeSettings settings)
    {
        List<ShortcutEntry> shortcuts = ActiveShortcuts(settings);
        ShortcutBindingSet bindings = ShortcutBindingSet.Create(shortcuts);
        List<(int Id, ShortcutEntry Shortcut)> releases;
        lock (_gate)
        {
            releases = PressedReleaseShortcuts();
            _shortcuts = bindings.Shortcuts;
            _pressedShortcuts.Clear();
        }

        foreach ((int id, ShortcutEntry shortcut) in releases)
        {
            Publish(id, shortcut, ShortcutPhase.Released);
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

    private List<ShortcutEntry> ActiveShortcuts(SteamInputBridgeSettings settings)
    {
        List<ShortcutEntry> shortcuts = [.. settings.Shortcuts];

        string? profileId = _profiles.ActiveProfile?.Id;
        if (!string.IsNullOrWhiteSpace(profileId) &&
            settings.Games.TryGetValue(profileId, out GameProfile? profile))
        {
            foreach (ShortcutEntry shortcut in profile.Shortcuts)
            {
                shortcuts.Add(shortcut);
            }
        }

        return shortcuts;
    }

    private List<(int Id, ShortcutEntry Shortcut)> PressedReleaseShortcuts()
    {
        List<(int Id, ShortcutEntry Shortcut)> releases = [];
        foreach (int id in _pressedShortcuts)
        {
            if (!_shortcuts.TryGetValue(id, out IReadOnlyList<ShortcutEntry>? shortcuts))
            {
                continue;
            }

            foreach (ShortcutEntry shortcut in shortcuts)
            {
                if (shortcut.Action is ShortcutValue.Enable or ShortcutValue.Disable)
                {
                    releases.Add((id, shortcut));
                }
            }
        }

        return releases;
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
            Publish(id, shortcut, ShortcutPhase.Pressed);
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
            if (shortcut.Action is ShortcutValue.Enable or ShortcutValue.Disable)
            {
                Publish(id, shortcut, ShortcutPhase.Released);
            }
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

    private void Publish(int id, ShortcutEntry shortcut, ShortcutPhase phase)
    {
        if (!shortcut.Target.HasValue)
        {
            return;
        }

        Shortcut?.Invoke(this, new(id, shortcut.Keys, shortcut.Target.Value, shortcut.Action, phase));
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
                    if (!entry.Target.HasValue)
                    {
                        continue;
                    }

                    status.Add(new(
                        entry.Keys,
                        entry.Target.Value.ToString(),
                        entry.Action.ToString(),
                        _pressedShortcuts.Contains(shortcutId)));
                }
            }

            return status;
        }
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
