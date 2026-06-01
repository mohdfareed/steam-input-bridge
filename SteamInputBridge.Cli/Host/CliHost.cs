using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Hosting;
using SteamInputBridge.Settings;

namespace SteamInputBridge.Cli.Host;

// MARK: Dependency Injection
// ============================================================================

internal static class CliHost
{
    public static IHost CreateCli()
    {
        return BridgeHost.CreateSettings(ConfigureLogging);
    }

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
        IHostEnvironment environment)
    {
        LoggingSettings settings = new();
        configuration.GetSection(LoggingSettings.SectionName).Bind(settings);

        _ = logging.ClearProviders();
        _ = logging.AddConsole();
        _ = logging.AddCliFileLogger(environment.ContentRootPath);
        _ = logging.SetMinimumLevel(settings.Level);
    }
}
