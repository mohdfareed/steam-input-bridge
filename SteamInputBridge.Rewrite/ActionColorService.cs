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
    private readonly ShortcutService _shortcuts;
    private readonly List<ActionColorSource> _sources = [];
    private string? _color;
    private bool _disposed;

    // MARK: Lifecycle
    // ========================================================================

    internal ActionColorService(ShortcutService shortcuts)
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

        ActionColorSource source = new(args.ShortcutId, args.Target.Color, Enabled: args.Action != ShortcutValue.Disable);
        if (args.Action == ShortcutValue.Enable)
        {
            SetSource(source, active: args.Phase == ShortcutPhase.Pressed);
        }
        else if (args.Action == ShortcutValue.Disable)
        {
            SetSource(source, active: args.Phase == ShortcutPhase.Pressed);
        }
        else if (args.Action == ShortcutValue.Toggle && args.Phase == ShortcutPhase.Pressed)
        {
            ToggleSource(source);
        }
    }

    private void ToggleSource(ActionColorSource source)
    {
        lock (_gate)
        {
            _ = _sources.Remove(source);
            if (!_sources.Contains(source))
            {
                _sources.Add(source);
            }
        }

        Publish();
    }

    private void SetSource(ActionColorSource source, bool active)
    {
        lock (_gate)
        {
            _ = _sources.Remove(source);
            if (active)
            {
                _sources.Add(source);
            }
        }

        Publish();
    }

    private void Publish()
    {
        string? color;
        bool changed;
        lock (_gate)
        {
            color = _sources.Count == 0 || !_sources[^1].Enabled ? null : _sources[^1].Color;
            changed = color != _color;
            _color = color;
        }

        if (changed)
        {
            ColorChanged?.Invoke(this, new(color));
        }
    }

    private readonly record struct ActionColorSource(int ShortcutId, string Color, bool Enabled);
}

/// <summary>Action color change event data.</summary>
/// <param name="color">Current displayed action color, or null when no color is active.</param>
public sealed class ActionColorChangedEventArgs(string? color) : EventArgs
{
    /// <summary>Current displayed action color, or null when no color is active.</summary>
    public string? Color { get; } = color;
}
