using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SteamInputBridge.App.Host;
using SteamInputBridge.Diagnostics;

namespace SteamInputBridge.App;

// MARK: Dependency Injection
// ============================================================================

/// <summary>File logging registration for application composition.</summary>
internal static class FileLogging
{
    /// <summary>Adds file logging beside the application executable.</summary>
    public static ILoggingBuilder AddApplicationFileLogger(this ILoggingBuilder logging, AppEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(logging);
        ArgumentNullException.ThrowIfNull(environment);

        _ = logging.Services.AddSingleton(environment);
        _ = logging.Services.AddSingleton<ILoggerProvider>(_ => new FileLoggerProvider(environment.LogPath));
        return logging;
    }
}
