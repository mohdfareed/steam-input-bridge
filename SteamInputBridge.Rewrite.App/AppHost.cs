using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Hosting;
using SteamInputBridge.Settings;

namespace SteamInputBridge.App;

// MARK: Dependency Injection
// ============================================================================

internal static class AppHost
{
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
        _ = logging.AddApplicationFileLogger();
        _ = logging.SetMinimumLevel(settings.Level);
    }
}
