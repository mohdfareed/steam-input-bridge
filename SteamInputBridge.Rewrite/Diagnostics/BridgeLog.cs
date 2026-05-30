using System;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Settings;

namespace SteamInputBridge.Diagnostics;

internal static class BridgeLog
{
    private static readonly Action<ILogger, string, Exception?> LogSettingsLoaded =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(1, nameof(LogSettingsLoaded)),
            "Settings loaded from file: {FilePath}");

    private static readonly Action<ILogger, int, int, Exception?> LogSettingsReloaded =
        LoggerMessage.Define<int, int>(
            LogLevel.Information,
            new EventId(2, nameof(LogSettingsReloaded)),
            "Settings reloaded with {ProfileCount} profile(s) and {ShortcutCount} shortcut(s).");

    private static readonly Action<ILogger, string, Exception?> LogSettingsReloadRejected =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(3, nameof(LogSettingsReloadRejected)),
            "Settings reload rejected: {ValidationErrors}");

    private static readonly Action<ILogger, int, int, Exception?> LogServerStarted =
        LoggerMessage.Define<int, int>(
            LogLevel.Information,
            new EventId(4, nameof(LogServerStarted)),
            "Server running with {ProfileCount} profile(s) and {ShortcutCount} shortcut(s).");

    private static readonly Action<ILogger, int, int, Exception?> LogServerSettingsApplied =
        LoggerMessage.Define<int, int>(
            LogLevel.Information,
            new EventId(5, nameof(LogServerSettingsApplied)),
            "Server settings applied with {ProfileCount} profile(s) and {ShortcutCount} shortcut(s).");

    private static readonly Action<ILogger, Exception?> LogServerStopped =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(6, nameof(LogServerStopped)),
            "Server stopped.");

    public static void SettingsLoaded(ILogger logger, string filePath)
    {
        LogSettingsLoaded(logger, filePath, null);
    }

    public static void SettingsReloaded(ILogger logger, SteamInputBridgeSettings settings)
    {
        LogSettingsReloaded(logger, settings.Games.Count, settings.Shortcuts.Count, null);
    }

    public static void SettingsReloadRejected(ILogger logger, string validationErrors)
    {
        LogSettingsReloadRejected(logger, validationErrors, null);
    }

    public static void ServerStarted(ILogger logger, SteamInputBridgeSettings settings)
    {
        LogServerStarted(logger, settings.Games.Count, settings.Shortcuts.Count, null);
    }

    public static void ServerSettingsApplied(ILogger logger, SteamInputBridgeSettings settings)
    {
        LogServerSettingsApplied(logger, settings.Games.Count, settings.Shortcuts.Count, null);
    }

    public static void ServerStopped(ILogger logger)
    {
        LogServerStopped(logger, null);
    }
}
