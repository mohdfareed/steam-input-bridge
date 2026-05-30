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

    private static readonly Action<ILogger, string, Exception?> LogSettingsReloaded =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(2, nameof(LogSettingsReloaded)),
            "Settings reloaded from file: {FilePath}");

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

    private static readonly Action<ILogger, string, Exception?> LogServerListening =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(7, nameof(LogServerListening)),
            "Server listening on control pipe {PipeName}.");

    private static readonly Action<ILogger, int, string, Exception?> LogClientConnectAttempted =
        LoggerMessage.Define<int, string>(
            LogLevel.Information,
            new EventId(8, nameof(LogClientConnectAttempted)),
            "Client process {ProcessId} attempted to connect for profile {ProfileId}.");

    private static readonly Action<ILogger, string, Exception?> LogClientControlPipeClosed =
        LoggerMessage.Define<string>(
            LogLevel.Debug,
            new EventId(9, nameof(LogClientControlPipeClosed)),
            "Client control pipe closed: {Message}");

    private static readonly Action<ILogger, string, Exception?> LogClientStarted =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(10, nameof(LogClientStarted)),
            "Client started for profile {ProfileId}.");

    private static readonly Action<ILogger, string, Exception?> LogClientStopped =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(11, nameof(LogClientStopped)),
            "Client stopped for profile {ProfileId}.");

    private static readonly Action<ILogger, string, Exception?> LogClientConnecting =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(12, nameof(LogClientConnecting)),
            "Client connecting to control pipe {PipeName}.");

    private static readonly Action<ILogger, string, Exception?> LogClientConnected =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(13, nameof(LogClientConnected)),
            "Client connected to control pipe {PipeName}.");

    private static readonly Action<ILogger, string, Exception?> LogClientConnectionFailed =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(14, nameof(LogClientConnectionFailed)),
            "Client connection failed: {Message}");

    private static readonly Action<ILogger, int, string, Exception?> LogClientDisconnected =
        LoggerMessage.Define<int, string>(
            LogLevel.Information,
            new EventId(15, nameof(LogClientDisconnected)),
            "Client process {ProcessId} disconnected from profile {ProfileId}.");

    public static void SettingsLoaded(ILogger logger, SettingsFile settingsFile)
    {
        LogSettingsLoaded(logger, settingsFile.Path, null);
    }

    public static void SettingsReloaded(ILogger logger, SettingsFile settingsFile)
    {
        LogSettingsReloaded(logger, settingsFile.Path, null);
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

    public static void ServerListening(ILogger logger, string pipeName)
    {
        LogServerListening(logger, pipeName, null);
    }

    public static void ClientConnectAttempted(ILogger logger, int processId, string profileId)
    {
        LogClientConnectAttempted(logger, processId, profileId, null);
    }

    public static void ClientControlPipeClosed(ILogger logger, string message)
    {
        LogClientControlPipeClosed(logger, message, null);
    }

    public static void ClientStarted(ILogger logger, string profileId)
    {
        LogClientStarted(logger, profileId, null);
    }

    public static void ClientStopped(ILogger logger, string profileId)
    {
        LogClientStopped(logger, profileId, null);
    }

    public static void ClientConnecting(ILogger logger, string pipeName)
    {
        LogClientConnecting(logger, pipeName, null);
    }

    public static void ClientConnected(ILogger logger, string pipeName)
    {
        LogClientConnected(logger, pipeName, null);
    }

    public static void ClientConnectionFailed(ILogger logger, string message)
    {
        LogClientConnectionFailed(logger, message, null);
    }

    public static void ClientDisconnected(ILogger logger, int processId, string profileId)
    {
        LogClientDisconnected(logger, processId, profileId, null);
    }
}
