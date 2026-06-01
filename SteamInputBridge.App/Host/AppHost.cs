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
    public static IHost CreateServer()
    {
        return BridgeHost.CreateServer(ConfigureLogging);
    }

    public static IHost CreateClient(string profileId)
    {
        return BridgeHost.CreateClient(profileId, ConfigureLogging);
    }

    private static void ConfigureLogging(
        ILoggingBuilder logging,
        ConfigurationManager configuration,
        IHostEnvironment hostEnvironment)
    {
        LoggingSettings settings = new();
        configuration.GetSection(LoggingSettings.SectionName).Bind(settings);

        _ = logging.ClearProviders();
        _ = logging.AddApplicationFileLogger(CreateEnvironment(hostEnvironment.ContentRootPath));
        _ = logging.SetMinimumLevel(settings.Level);
    }

    private static AppEnvironment CreateEnvironment(string contentRootPath)
    {
        string baseDirectory = AppContext.BaseDirectory;
        string executablePath = System.Environment.ProcessPath ??
            ProductMetadata.ResolveAppExecutablePath(baseDirectory);
        string settingsPath = Path.Combine(contentRootPath, "appsettings.json");
        string logPath = ResolveRunLogFilePath(contentRootPath);
        string version = ProductMetadata.Version(Assembly.GetExecutingAssembly());
        return new AppEnvironment(contentRootPath, executablePath, settingsPath, logPath, version);
    }

    private static string ResolveRunLogFilePath(string contentRootPath)
    {
        string directory = Path.Combine(contentRootPath, "logs");
        string start = Process.GetCurrentProcess().StartTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string fileName = $"{start}-{System.Environment.ProcessId.ToString(CultureInfo.InvariantCulture)}.log";
        return Path.Combine(directory, fileName);
    }
}
