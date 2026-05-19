using System.IO;
using Microsoft.Extensions.DependencyInjection;
using VirtualMouse.Settings;
using VirtualMouse.Settings.Profiles;
using VirtualMouse.Steam;

namespace VirtualMouse.Tray;

internal static class SrmExportAction
{
    public static SrmExportResult Export(IServiceProvider services)
    {
        try
        {
            ProfilesService profiles = services.GetRequiredService<ProfilesService>();
            ApplicationSettingsService settings =
                services.GetRequiredService<ApplicationSettingsService>();
            SettingsFile settingsFile = services.GetRequiredService<SettingsFile>();

            string manifestPath = ResolveManifestPath(
                settings.Current.Steam.SrmExportPath,
                settingsFile.Path);

            string shortcutPath = Path.Combine(
                System.AppContext.BaseDirectory,
                "VirtualMouse.exe");
            string manifest = SteamRomManagerExport.CreateJson(profiles, shortcutPath);

            File.WriteAllText(manifestPath, manifest);
            return SrmExportResult.Success(profiles.ListProfileIds().Count);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return SrmExportResult.Failure(exception.Message);
        }
    }

    private static string ResolveManifestPath(string? path, string settingsPath)
    {
        string filePath = Environment.ExpandEnvironmentVariables(
            string.IsNullOrWhiteSpace(path) ? "srm-manifest.json" : path);
        return Path.IsPathFullyQualified(filePath)
            ? filePath
            : Path.Combine(
                Path.GetDirectoryName(settingsPath) ?? System.AppContext.BaseDirectory,
                filePath);
    }
}

internal sealed record SrmExportResult(bool Exported, int ProfileCount, string? Error)
{
    public static SrmExportResult Success(int profileCount)
    {
        return new SrmExportResult(true, profileCount, null);
    }

    public static SrmExportResult Failure(string error)
    {
        return new SrmExportResult(false, 0, error);
    }
}
