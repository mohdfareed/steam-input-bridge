using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VirtualMouse.Hosting;
using VirtualMouse.Settings;
using VirtualMouse.Settings.Profiles;

namespace VirtualMouse.Shortcut;

internal static class ShortcutMode
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length is 0 or > 4)
        {
            return 2;
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

            return 2;
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
        string settingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        _ = builder.Configuration.AddJsonFile(settingsPath, optional: true, reloadOnChange: true);

        _ = builder.Services.AddApplicationSettings(builder.Configuration, settingsPath);
        _ = builder.Services.AddApplicationClient();
        _ = builder.Services.AddProfiles();

        VirtualMouseSettings settings = new();
        builder.Configuration.GetSection(VirtualMouseSettings.SectionName).Bind(settings);
        _ = builder.Logging.AddApplicationFileLogger(
            ResolveLogFilePath(settingsPath, settings.Logging.LogFile));

        return builder.Build();
    }

    private static string? ResolveLogFilePath(string settingsPath, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (Path.IsPathFullyQualified(path))
        {
            return path;
        }

        string settingsDirectory = Path.GetDirectoryName(settingsPath) ?? AppContext.BaseDirectory;
        return Path.Combine(settingsDirectory, path);
    }
}
