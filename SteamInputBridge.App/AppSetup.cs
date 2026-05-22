using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Hosting.Client.Connection;
using SteamInputBridge.Hosting.Server;
using SteamInputBridge.Settings;
using SteamInputBridge.Settings.Profiles;

namespace SteamInputBridge.App;

internal static class AppSetup
{
    public static IHost CreateCli()
    {
        return Create(
            addClient: true,
            addServer: true,
            addConsoleLogger: true,
            clearLoggingProviders: false);
    }

    public static IHost CreateShortcut()
    {
        return Create(
            addClient: true,
            addServer: false,
            addConsoleLogger: false,
            clearLoggingProviders: true);
    }

    public static IHost CreateTray()
    {
        return Create(
            addClient: false,
            addServer: true,
            addConsoleLogger: false,
            clearLoggingProviders: true);
    }

    public static string ResolveSettingsPath()
    {
        return Path.Combine(System.AppContext.BaseDirectory, "appsettings.json");
    }

    public static string? ResolveLogDirectory(string settingsPath, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (Path.IsPathFullyQualified(path))
        {
            return path;
        }

        string settingsDirectory = Path.GetDirectoryName(settingsPath) ?? System.AppContext.BaseDirectory;
        return Path.Combine(settingsDirectory, path);
    }

    private static IHost Create(
        bool addClient,
        bool addServer,
        bool addConsoleLogger,
        bool clearLoggingProviders)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        string settingsPath = ResolveSettingsPath();
        _ = builder.Configuration.AddJsonFile(settingsPath, optional: true, reloadOnChange: true);

        _ = builder.Services.AddApplicationSettings(builder.Configuration, settingsPath);
        if (addClient)
        {
            _ = builder.Services.AddApplicationClient();
        }

        if (addServer)
        {
            _ = builder.Services.AddApplicationServer();
        }

        _ = builder.Services.AddProfiles();

        SteamInputBridgeSettings settings = new();
        builder.Configuration.GetSection(SteamInputBridgeSettings.SectionName).Bind(settings);
        if (clearLoggingProviders)
        {
            _ = builder.Logging.ClearProviders();
        }

        _ = builder.Logging.SetMinimumLevel(settings.Logging.Level);
        if (addConsoleLogger)
        {
            _ = builder.Logging.AddConsole();
        }

        _ = builder.Logging.AddApplicationFileLogger(
            ResolveLogDirectory(settingsPath, settings.Logging.LogDirectory));

        return builder.Build();
    }
}
