using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Hosting.Server;
using SteamInputBridge.Settings;

namespace SteamInputBridge.App.Hosting;

internal static class AppHost
{
    public static IHost CreateServer()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        string settingsPath = System.IO.Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        _ = builder.Configuration
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile(settingsPath, optional: true, reloadOnChange: true);

        _ = builder.Logging.ClearProviders();
        _ = builder.Logging.AddConsole();
        _ = builder.Logging.SetMinimumLevel(GetConfiguredLogLevel(builder.Configuration));

        _ = builder.Services.AddBridgeServer(builder.Configuration, settingsPath);

        return builder.Build();
    }

    private static LogLevel GetConfiguredLogLevel(ConfigurationManager configuration)
    {
        LoggingSettings logging = new();
        configuration.GetSection(LoggingSettings.SectionName).Bind(logging);
        return logging.Level;
    }
}
