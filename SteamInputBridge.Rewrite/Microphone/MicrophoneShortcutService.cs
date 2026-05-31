using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Shortcuts;
using SteamInputBridge.Shortcuts.Runtime;

namespace SteamInputBridge.Microphone;

/// <summary>Applies microphone shortcut events to the system microphone.</summary>
public sealed class MicrophoneShortcutService(
    ShortcutService shortcuts,
    MicrophoneService microphone,
    ILogger<MicrophoneShortcutService> logger) : IHostedService, IDisposable
{
    private readonly Lock _gate = new();
    private readonly List<MicrophoneHold> _holds = [];
    private bool _disposed;

    // MARK: Lifecycle
    // ========================================================================

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        shortcuts.Shortcut += OnShortcut;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        shortcuts.Shortcut -= OnShortcut;
        ClearHolds();
        return Task.CompletedTask;
    }

    /// <summary>Stops listening for microphone shortcut events.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        shortcuts.Shortcut -= OnShortcut;
    }

    // MARK: Events
    // ========================================================================

    private void OnShortcut(object? sender, ShortcutEventArgs args)
    {
        _ = sender;
        if (args.Target.Target != ShortcutTarget.Microphone)
        {
            return;
        }

        if (args.Phase == ShortcutPhase.Pressed)
        {
            ApplyPressed(args.ShortcutId, args.Action);
        }
        else
        {
            ApplyReleased(args.ShortcutId, args.Action);
        }
    }

    private void ApplyPressed(int shortcutId, ShortcutValue action)
    {
        switch (action)
        {
            case ShortcutValue.Toggle:
                SetMicrophoneEnabled(!IsMicrophoneEnabled());
                break;
            case ShortcutValue.Enable:
                Hold(shortcutId, enabled: true);
                break;
            case ShortcutValue.Disable:
                Hold(shortcutId, enabled: false);
                break;
            default:
                break;
        }
    }

    private void ApplyReleased(int shortcutId, ShortcutValue action)
    {
        if (action == ShortcutValue.Enable)
        {
            Release(shortcutId, enabled: false);
        }
        else if (action == ShortcutValue.Disable)
        {
            Release(shortcutId, enabled: true);
        }
    }

    // MARK: Holds
    // ========================================================================

    private void Hold(int shortcutId, bool enabled)
    {
        lock (_gate)
        {
            _ = _holds.RemoveAll(hold => hold.ShortcutId == shortcutId);
            _holds.Add(new(shortcutId, enabled));
        }

        SetCurrentHold();
    }

    private void Release(int shortcutId, bool enabled)
    {
        lock (_gate)
        {
            _ = _holds.RemoveAll(hold => hold.ShortcutId == shortcutId);
        }

        SetCurrentHold(enabled);
    }

    private void SetCurrentHold(bool releasedEnabled)
    {
        bool enabled;
        lock (_gate)
        {
            enabled = _holds.Count == 0 ? releasedEnabled : _holds[^1].Enabled;
        }

        SetMicrophoneEnabled(enabled);
    }

    private void SetCurrentHold()
    {
        bool enabled;
        lock (_gate)
        {
            enabled = _holds[^1].Enabled;
        }

        SetMicrophoneEnabled(enabled);
    }

    private void ClearHolds()
    {
        bool? enabled = null;
        lock (_gate)
        {
            if (_holds.Count != 0)
            {
                enabled = !_holds[^1].Enabled;
            }

            _holds.Clear();
        }

        if (enabled.HasValue)
        {
            SetMicrophoneEnabled(enabled.Value);
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

    private static readonly Action<ILogger, string, Exception?> LogMicrophoneShortcutFailed =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(1, nameof(LogMicrophoneShortcutFailed)),
            "Microphone shortcut failed: {Message}");
}
