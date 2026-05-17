using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VirtualMouse.Hosting;
using VirtualMouse.Settings;
using VirtualMouse.Settings.Profiles;

namespace Refactor.Cli;

internal static class AppSetup
{
    public static IHost Create()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        string settingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

        // App-owned settings live under VirtualMouse; top-level Logging is reserved for Microsoft logging.
        _ = builder.Configuration.AddJsonFile(settingsPath, optional: true, reloadOnChange: true);

        _ = builder.Services.AddApplicationSettings(builder.Configuration, settingsPath);
        _ = builder.Services.AddApplicationClient();
        _ = builder.Services.AddApplicationServer();
        _ = builder.Services.AddProfiles();
        _ = builder.Logging.AddConsole();
        VirtualMouseSettings settings = new();
        builder.Configuration.GetSection(VirtualMouseSettings.SectionName).Bind(settings);
        _ = builder.Logging.AddApplicationFileLogger(ResolveConfiguredPath(settingsPath, settings.Logging.LogFile));

        return builder.Build();
    }

    private static string? ResolveConfiguredPath(string settingsPath, string? path)
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
