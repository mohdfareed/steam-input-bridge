using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Hosting;
using SteamInputBridge.Settings;

namespace SteamInputBridge.App.Host;

// MARK: Dependency Injection
// ============================================================================

internal static class AppHost
{
    private static readonly Lazy<AppEnvironment> Environment = new(CreateEnvironment);

    public static IHost CreateServer()
    {
        return BridgeHost.CreateServer(AppContext.BaseDirectory, ConfigureLogging);
    }

    public static IHost CreateClient(string profileId)
    {
        return BridgeHost.CreateClient(AppContext.BaseDirectory, profileId, ConfigureLogging);
    }

    private static void ConfigureLogging(ILoggingBuilder logging, ConfigurationManager configuration)
    {
        LoggingSettings settings = new();
        configuration.GetSection(LoggingSettings.SectionName).Bind(settings);

        _ = logging.ClearProviders();
        _ = logging.AddApplicationFileLogger(Environment.Value);
        _ = logging.SetMinimumLevel(settings.Level);
    }

    private static AppEnvironment CreateEnvironment()
    {
        string baseDirectory = AppContext.BaseDirectory;
        string executablePath = System.Environment.ProcessPath ??
            ProductMetadata.ResolveAppExecutablePath(baseDirectory);
        string settingsPath = Path.Combine(baseDirectory, "appsettings.json");
        string logPath = ResolveRunLogFilePath(baseDirectory);
        string version = ProductMetadata.Version(Assembly.GetExecutingAssembly());
        return new AppEnvironment(baseDirectory, executablePath, settingsPath, logPath, version);
    }

    private static string ResolveRunLogFilePath(string baseDirectory)
    {
        string directory = Path.Combine(baseDirectory, "logs");
        string start = Process.GetCurrentProcess().StartTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string fileName = $"{start}-{System.Environment.ProcessId.ToString(CultureInfo.InvariantCulture)}.log";
        return Path.Combine(directory, fileName);
    }
}
