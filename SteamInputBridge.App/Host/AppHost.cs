using System;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Diagnostics;
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
        SettingsFile settingsFile)
    {
        LoggingSettings settings = new();
        configuration.GetSection(LoggingSettings.SectionName).Bind(settings);

        _ = logging.ClearProviders();
        _ = logging.AddApplicationFileLogger(CreateEnvironment(settingsFile, settings.LogDirectory));
        _ = logging.SetMinimumLevel(settings.Level);
    }

    private static AppEnvironment CreateEnvironment(SettingsFile settingsFile, string logDirectory)
    {
        string baseDirectory = AppContext.BaseDirectory;
        string executablePath = System.Environment.ProcessPath ??
            ProductMetadata.ResolveAppExecutablePath(baseDirectory);
        string logPath = FileLoggerProvider.CreateLogPath(settingsFile, logDirectory: logDirectory);
        string version = ProductMetadata.Version(Assembly.GetExecutingAssembly());
        return new AppEnvironment(baseDirectory, executablePath, settingsFile.Path, logPath, version);
    }
}
