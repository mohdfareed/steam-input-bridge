using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VirtualMouse.Hosting;
using VirtualMouse.Settings;
using VirtualMouse.Settings.Profiles;

namespace VirtualMouse.Tray;

internal static class AppSetup
{
    public static IHost Create()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        string settingsPath = Path.Combine(System.AppContext.BaseDirectory, "appsettings.json");
        _ = builder.Configuration.AddJsonFile(settingsPath, optional: true, reloadOnChange: true);

        _ = builder.Services.AddApplicationSettings(builder.Configuration, settingsPath);
        _ = builder.Services.AddApplicationServer();
        _ = builder.Services.AddProfiles();

        VirtualMouseSettings settings = new();
        builder.Configuration.GetSection(VirtualMouseSettings.SectionName).Bind(settings);
        _ = builder.Logging.ClearProviders();
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

        string settingsDirectory = Path.GetDirectoryName(settingsPath) ?? System.AppContext.BaseDirectory;
        return Path.Combine(settingsDirectory, path);
    }
}
