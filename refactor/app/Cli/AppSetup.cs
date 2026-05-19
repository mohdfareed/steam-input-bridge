using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VirtualMouse.Hosting;
using VirtualMouse.Settings;
using VirtualMouse.Settings.Profiles;

namespace VirtualMouse.Cli;

internal static class AppSetup
{
    public static IHost Create()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();

        // App-owned settings live under VirtualMouse; top-level Logging is reserved for Microsoft logging.
        string settingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        _ = builder.Configuration.AddJsonFile(settingsPath, optional: true, reloadOnChange: true);

        // Register settings
        _ = builder.Services.AddApplicationSettings(builder.Configuration, settingsPath);
        _ = builder.Services.AddApplicationClient();
        _ = builder.Services.AddApplicationServer();
        _ = builder.Services.AddProfiles();

        // Configure settings
        VirtualMouseSettings settings = new();
        builder.Configuration.GetSection(VirtualMouseSettings.SectionName).Bind(settings);

        // Configure logging
        _ = builder.Logging.AddConsole();
        _ = builder.Logging.AddApplicationFileLogger(ResolveLogFilePath(settingsPath, settings.Logging.LogFile));

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
