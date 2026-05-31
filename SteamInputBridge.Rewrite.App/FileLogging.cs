using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Diagnostics;

namespace SteamInputBridge.App;

// MARK: Dependency Injection
// ============================================================================

/// <summary>File logging registration for application composition.</summary>
internal static class FileLogging
{
    /// <summary>Adds file logging beside the application executable.</summary>
    public static ILoggingBuilder AddApplicationFileLogger(this ILoggingBuilder logging)
    {
        ArgumentNullException.ThrowIfNull(logging);

        _ = logging.Services.AddSingleton<ILoggerProvider>(_ => new FileLoggerProvider(ResolveRunLogFilePath()));
        return logging;
    }

    /// <summary>Gets the per-process log path beside the application executable.</summary>
    public static string ResolveRunLogFilePath()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "logs");
        string start = Process.GetCurrentProcess().StartTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string fileName = $"{start}-{Environment.ProcessId.ToString(CultureInfo.InvariantCulture)}.log";
        return Path.Combine(directory, fileName);
    }
}
