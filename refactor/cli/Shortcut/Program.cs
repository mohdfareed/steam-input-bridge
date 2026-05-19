using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VirtualMouse.Hosting;
using VirtualMouse.Settings;
using VirtualMouse.Settings.Profiles;

return await RunAsync(args).ConfigureAwait(false);

static async Task<int> RunAsync(string[] args)
{
    if (args.Length is 0 or > 3)
    {
        return 2;
    }

    string profileId = args[0];
    uint? appId = null;
    if (args.Length == 3)
    {
        if (!string.Equals(args[1], "--app-id", StringComparison.OrdinalIgnoreCase) ||
            !uint.TryParse(args[2], out uint parsedAppId))
        {
            return 2;
        }

        appId = parsedAppId;
    }

    using IHost app = CreateApp();
    GameClient game = app.Services.GetRequiredService<GameClient>();
    await using (game.ConfigureAwait(false))
    {
        await game.RunAsync(profileId, appId, CancellationToken.None).ConfigureAwait(false);
    }

    return 0;
}

static IHost CreateApp()
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

static string? ResolveLogFilePath(string settingsPath, string? path)
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
