using System;
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
    private readonly ShortcutSwitch _switch = new();
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
        _switch.Reset();
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

        bool enabled = _switch.Apply(args.ShortcutId, args.Action, args.Phase, IsMicrophoneEnabled());
        SetMicrophoneEnabled(enabled);
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

    // MARK: Logging
    // ========================================================================

    private static readonly Action<ILogger, string, Exception?> LogMicrophoneShortcutFailed =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(1, nameof(LogMicrophoneShortcutFailed)),
            "Microphone shortcut failed: {Message}");
}
