using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Hosting.Client;
using SteamInputBridge.Hosting.Server;
using SteamInputBridge.Settings;

namespace SteamInputBridge.App.Hosting;

internal static class AppHost
{
    public static IHost CreateCli()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        string settingsPath = System.IO.Path.Combine(AppContext.BaseDirectory, "appsettings.json");

        _ = builder.Configuration
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile(settingsPath, optional: true, reloadOnChange: true);

        ConfigureLogging(builder.Logging, builder.Configuration);

        _ = builder.Services.AddApplicationSettings(builder.Configuration, settingsPath);

        return builder.Build();
    }

    public static IHost CreateServer()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        string settingsPath = System.IO.Path.Combine(AppContext.BaseDirectory, "appsettings.json");

        _ = builder.Configuration
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile(settingsPath, optional: true, reloadOnChange: true);

        ConfigureLogging(builder.Logging, builder.Configuration);

        _ = builder.Services.AddBridgeServer(builder.Configuration, settingsPath);

        return builder.Build();
    }

    public static IHost CreateClient(string profileId)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        string settingsPath = System.IO.Path.Combine(AppContext.BaseDirectory, "appsettings.json");

        _ = builder.Configuration
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile(settingsPath, optional: true, reloadOnChange: true);

        ConfigureLogging(builder.Logging, builder.Configuration);

        _ = builder.Services.AddBridgeClient(profileId);
        return builder.Build();
    }

    private static void ConfigureLogging(ILoggingBuilder logging, ConfigurationManager configuration)
    {
        LoggingSettings settings = new();
        configuration.GetSection(LoggingSettings.SectionName).Bind(settings);

        _ = logging.ClearProviders();
        _ = logging.AddConsole();
        _ = logging.AddApplicationFileLogger();
        _ = logging.SetMinimumLevel(settings.Level);
    }
}
