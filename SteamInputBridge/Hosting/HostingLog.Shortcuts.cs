using Microsoft.Extensions.Logging;
using SteamInputBridge.Settings;

namespace SteamInputBridge.Hosting;

internal static partial class HostingLog
{
    [LoggerMessage(EventId = 35, Level = LogLevel.Information, Message = "Registered keyboard shortcuts: {Count}")]
    public static partial void ShortcutsRegistered(ILogger logger, int count);

    [LoggerMessage(EventId = 36, Level = LogLevel.Warning, Message = "Skipped shortcut {Name}: {Message}")]
    public static partial void ShortcutSkipped(ILogger logger, string name, string message);

    [LoggerMessage(EventId = 37, Level = LogLevel.Warning, Message = "Keyboard shortcut registration failed: {Message}")]
    public static partial void ShortcutRegistrationFailed(ILogger logger, string message);

    [LoggerMessage(EventId = 38, Level = LogLevel.Information, Message = "Applied shortcut {Name}: {Target}={Value}")]
    public static partial void ShortcutApplied(
        ILogger logger,
        string name,
        ShortcutTargetSpec target,
        ShortcutValue value);

    [LoggerMessage(EventId = 39, Level = LogLevel.Warning, Message = "Could not set microphone shortcut state: {Message}")]
    public static partial void MicrophoneShortcutFailed(ILogger logger, string message);
}
