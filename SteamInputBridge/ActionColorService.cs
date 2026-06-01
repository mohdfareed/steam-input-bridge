using System;
using System.Collections.Generic;
using System.Threading;
using SteamInputBridge.Shortcuts;
using SteamInputBridge.Shortcuts.Runtime;

namespace SteamInputBridge;

/// <summary>Maintains the currently displayed action color from shortcut events.</summary>
public sealed class ActionColorService : IDisposable
{
    private readonly Lock _gate = new();
    private readonly IShortcutSource _shortcuts;
    private readonly Dictionary<string, ShortcutSwitch> _switches = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ActionColorSource> _sources = [];
    private string? _color;
    private bool _disposed;

    // MARK: Lifecycle
    // ========================================================================

    /// <summary>Creates the action color service.</summary>
    public ActionColorService(ShortcutService shortcuts)
        : this(new ShortcutServiceSource(shortcuts ?? throw new ArgumentNullException(nameof(shortcuts))))
    {
    }

    internal ActionColorService(IShortcutSource shortcuts)
    {
        ArgumentNullException.ThrowIfNull(shortcuts);

        _shortcuts = shortcuts;
        _shortcuts.Shortcut += OnShortcut;
    }

    /// <summary>Raised when the displayed action color changes.</summary>
    public event EventHandler<ActionColorChangedEventArgs>? ColorChanged;

    /// <summary>Current displayed action color, or null when no color is active.</summary>
    public string? Color
    {
        get
        {
            lock (_gate)
            {
                return _color;
            }
        }
    }

    /// <summary>Stops listening for shortcut events.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _shortcuts.Shortcut -= OnShortcut;
    }

    // MARK: Events
    // ========================================================================

    private void OnShortcut(object? sender, ShortcutEventArgs args)
    {
        _ = sender;
        if (args.Target.Target != ShortcutTarget.ActionColor || string.IsNullOrWhiteSpace(args.Target.Color))
        {
            return;
        }

        string color = args.Target.Color;
        bool enabled = ApplySwitch(color, args.ShortcutId, args.Action, args.Phase);
        SetSource(new(color, enabled));
    }

    private bool ApplySwitch(string color, int shortcutId, ShortcutValue action, ShortcutPhase phase)
    {
        lock (_gate)
        {
            if (!_switches.TryGetValue(color, out ShortcutSwitch? shortcutSwitch))
            {
                shortcutSwitch = new();
                _switches[color] = shortcutSwitch;
            }

            return shortcutSwitch.Apply(shortcutId, action, phase, defaultEnabled: false);
        }
    }

    private void SetSource(ActionColorSource source)
    {
        lock (_gate)
        {
            _ = _sources.RemoveAll(existing => string.Equals(existing.Color, source.Color, StringComparison.OrdinalIgnoreCase));
            _sources.Add(source);
        }

        Publish();
    }

    private void Publish()
    {
        string? color;
        bool changed;
        lock (_gate)
        {
            color = ActiveColor();
            changed = color != _color;
            _color = color;
        }

        if (changed)
        {
            ColorChanged?.Invoke(this, new(color));
        }
    }

    private string? ActiveColor()
    {
        for (int i = _sources.Count - 1; i >= 0; i--)
        {
            if (_sources[i].Enabled)
            {
                return _sources[i].Color;
            }
        }

        return null;
    }

    private readonly record struct ActionColorSource(string Color, bool Enabled);
}

/// <summary>Action color change event data.</summary>
/// <param name="color">Current displayed action color, or null when no color is active.</param>
public sealed class ActionColorChangedEventArgs(string? color) : EventArgs
{
    /// <summary>Current displayed action color, or null when no color is active.</summary>
    public string? Color { get; } = color;
}
