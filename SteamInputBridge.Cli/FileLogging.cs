using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Diagnostics;
using SteamInputBridge.Settings;

namespace SteamInputBridge.Cli;

// MARK: Dependency Injection
// ============================================================================

/// <summary>File logging registration for CLI composition.</summary>
internal static class FileLogging
{
    /// <summary>Adds file logging for the current CLI process.</summary>
    public static ILoggingBuilder AddCliFileLogger(
        this ILoggingBuilder logging,
        SettingsFile settingsFile,
        string logDirectory)
    {
        ArgumentNullException.ThrowIfNull(logging);
        ArgumentNullException.ThrowIfNull(settingsFile);

        _ = logging.Services.AddSingleton<ILoggerProvider>(
            _ => new FileLoggerProvider(FileLoggerProvider.CreateLogPath(settingsFile, logDirectory: logDirectory)));
        return logging;
    }
}
