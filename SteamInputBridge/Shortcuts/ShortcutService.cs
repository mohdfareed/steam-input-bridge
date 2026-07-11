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

/// <summary>Registers configured shortcuts, tracks pressed shortcut state, and publishes shortcut actions.</summary>
public sealed partial class ShortcutService : IHostedService, IDisposable, IShortcutSource
{
    private readonly SettingsService _settings;
    private readonly ActiveProfileService _profiles;
    private readonly IGlobalShortcutListener _listener;
    private readonly ILogger<ShortcutService> _logger;
    private readonly Lock _gate = new();
    private readonly Lock _queueGate = new();
    private readonly Queue<KeyChange> _pendingKeys = [];
    private readonly Dictionary<int, KeyboardShortcut> _shortcutKeys = [];
    private readonly Dictionary<int, IReadOnlyList<ShortcutEntry>> _shortcutEntries = [];
    private readonly HashSet<ushort> _downKeys = [];
    private readonly Dictionary<ushort, long> _downAt = [];
    private readonly HashSet<int> _pressedShortcuts = [];
    private readonly HashSet<int> _desiredShortcuts = [];
    private readonly List<int> _scratchShortcutIds = [];
    private int _processingPendingKeys;
    private int _registrationVersion;
    private bool _disposed;

    /// <summary>Creates the shortcut service.</summary>
    public ShortcutService(
        SettingsService settings,
        ActiveProfileService profiles,
        ILogger<ShortcutService> logger)
        : this(settings, profiles, new GlobalShortcutListener(), logger)
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
    // ============================================================================

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
        _listener.Update([], static (_, _) => { });
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
    // ============================================================================

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
        Dictionary<KeyboardShortcut, int> idsByShortcut = [];
        Dictionary<int, KeyboardShortcut> shortcutKeys = [];
        Dictionary<int, List<ShortcutEntry>> entriesById = [];
        List<ShortcutEntry> activeShortcuts = [.. settings.Shortcuts];
        string? profileId = _profiles.ActiveProfile?.Id;
        if (!string.IsNullOrWhiteSpace(profileId) && settings.Games.TryGetValue(profileId, out GameProfile? profile))
        {
            activeShortcuts.AddRange(profile.Shortcuts);
        }

        foreach (ShortcutEntry shortcut in activeShortcuts)
        {
            KeyboardShortcut keys = KeyboardShortcutParser.Parse(shortcut.Keys);
            if (!idsByShortcut.TryGetValue(keys, out int id))
            {
                id = idsByShortcut.Count + 1;
                idsByShortcut[keys] = id;
                shortcutKeys[id] = keys;
                entriesById[id] = [];
            }

            entriesById[id].Add(shortcut);
        }

        Dictionary<int, IReadOnlyList<ShortcutEntry>> shortcutEntries = new(entriesById.Count);
        foreach ((int id, List<ShortcutEntry> entries) in entriesById)
        {
            shortcutEntries[id] = entries;
        }

        List<(int Id, ShortcutEntry Entry, ShortcutPhase Phase)> releases = [];
        int version = Interlocked.Increment(ref _registrationVersion);
        lock (_queueGate)
        {
            _pendingKeys.Clear();
        }

        lock (_gate)
        {
            foreach (int id in _pressedShortcuts)
            {
                if (!_shortcutEntries.TryGetValue(id, out IReadOnlyList<ShortcutEntry>? entries))
                {
                    continue;
                }

                foreach (ShortcutEntry entry in entries)
                {
                    if (entry.Action is ShortcutValue.Enable or ShortcutValue.Disable)
                    {
                        releases.Add((id, entry, ShortcutPhase.Released));
                    }
                }
            }

            _shortcutKeys.Clear();
            foreach ((int id, KeyboardShortcut keys) in shortcutKeys)
            {
                _shortcutKeys[id] = keys;
            }

            _shortcutEntries.Clear();
            foreach ((int id, IReadOnlyList<ShortcutEntry> entries) in shortcutEntries)
            {
                _shortcutEntries[id] = entries;
            }

            _downKeys.Clear();
            _downAt.Clear();
            _pressedShortcuts.Clear();
            _desiredShortcuts.Clear();
        }

        foreach ((int id, ShortcutEntry entry, ShortcutPhase phase) in releases)
        {
            if (entry.Target.HasValue)
            {
                Shortcut?.Invoke(this, new(id, entry.Keys, entry.Target.Value, entry.Action, phase));
            }
        }

        HashSet<ushort> observedKeys = [];
        foreach (KeyboardShortcut shortcut in shortcutKeys.Values)
        {
            _ = observedKeys.Add(shortcut.VirtualKey);
        }

        AddModifierKeys(observedKeys);

        try
        {
            _listener.Update(observedKeys, OnKeyChanged);
            LogShortcutsRegistered(_logger, shortcutKeys.Count, null);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            LogShortcutRegistrationFailed(_logger, exception.Message, null);
        }

        List<(int Id, ShortcutEntry Entry, ShortcutPhase Phase)> currentPresses = [];
        lock (_gate)
        {
            if (version == Volatile.Read(ref _registrationVersion))
            {
                SeedCurrentlyDownKeys();
                _desiredShortcuts.Clear();
                AddDesiredPressedShortcuts();

                foreach (int id in _desiredShortcuts)
                {
                    _ = _pressedShortcuts.Add(id);
                    if (!_shortcutEntries.TryGetValue(id, out IReadOnlyList<ShortcutEntry>? entries))
                    {
                        continue;
                    }

                    foreach (ShortcutEntry entry in entries)
                    {
                        if (entry.Action is ShortcutValue.Enable or ShortcutValue.Disable)
                        {
                            currentPresses.Add((id, entry, ShortcutPhase.Pressed));
                        }
                    }
                }
            }
        }

        foreach ((int id, ShortcutEntry entry, ShortcutPhase phase) in currentPresses)
        {
            if (entry.Target.HasValue)
            {
                Shortcut?.Invoke(this, new(id, entry.Keys, entry.Target.Value, entry.Action, phase));
            }
        }

        StatusChanged?.Invoke(this, EventArgs.Empty);
    }
}
