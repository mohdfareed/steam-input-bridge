using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Hosting.Client;
using SteamInputBridge.Settings;
using SteamInputBridge.Settings.Profiles;

namespace SteamInputBridge.App.Shortcut;

internal static class ShortcutMode
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length is 0 or > 4)
        {
            throw new InvalidOperationException("Shortcut mode requires a profile id.");
        }

        string profileId = args[0];
        uint? appId = null;
        bool killReceivers = false;

        for (int i = 1; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--kill", StringComparison.OrdinalIgnoreCase))
            {
                killReceivers = true;
                continue;
            }

            if (string.Equals(args[i], "--app-id", StringComparison.OrdinalIgnoreCase) &&
                i + 1 < args.Length &&
                uint.TryParse(args[i + 1], out uint parsedAppId))
            {
                appId = parsedAppId;
                i++;
                continue;
            }

            throw new InvalidOperationException($"Invalid shortcut argument: {args[i]}");
        }

        using IHost app = CreateApp();
        GameClient game = app.Services.GetRequiredService<GameClient>();
        await using (game.ConfigureAwait(false))
        {
            await game.RunAsync(profileId, appId, killReceivers, CancellationToken.None)
                .ConfigureAwait(false);
        }

        return 0;
    }

    private static IHost CreateApp()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        string settingsPath = Path.Combine(System.AppContext.BaseDirectory, "appsettings.json");
        _ = builder.Configuration.AddJsonFile(settingsPath, optional: true, reloadOnChange: true);

        _ = builder.Services.AddApplicationSettings(builder.Configuration, settingsPath);
        _ = builder.Services.AddApplicationClient();
        _ = builder.Services.AddProfiles();

        SteamInputBridgeSettings settings = new();
        builder.Configuration.GetSection(SteamInputBridgeSettings.SectionName).Bind(settings);
        _ = builder.Logging.SetMinimumLevel(settings.Logging.Level);
        _ = builder.Logging.AddApplicationFileLogger(
            ResolveLogDirectory(settingsPath, settings.Logging.LogDirectory));

        return builder.Build();
    }

    private static string? ResolveLogDirectory(string settingsPath, string? path)
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
}
