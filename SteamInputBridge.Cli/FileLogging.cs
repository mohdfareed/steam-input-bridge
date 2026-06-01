using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Diagnostics;

namespace SteamInputBridge.Cli;

// MARK: Dependency Injection
// ============================================================================

/// <summary>File logging registration for CLI composition.</summary>
internal static class FileLogging
{
    /// <summary>Adds file logging under the Generic Host content root.</summary>
    public static ILoggingBuilder AddCliFileLogger(this ILoggingBuilder logging, string contentRootPath)
    {
        ArgumentNullException.ThrowIfNull(logging);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRootPath);

        _ = logging.Services.AddSingleton<ILoggerProvider>(_ => new FileLoggerProvider(ResolveRunLogFilePath(contentRootPath)));
        return logging;
    }

    /// <summary>Gets the per-process log path under the Generic Host content root.</summary>
    public static string ResolveRunLogFilePath(string contentRootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRootPath);

        string directory = Path.Combine(contentRootPath, "logs");
        string start = Process.GetCurrentProcess().StartTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string fileName = $"{start}-{Environment.ProcessId.ToString(CultureInfo.InvariantCulture)}.log";
        return Path.Combine(directory, fileName);
    }
}
