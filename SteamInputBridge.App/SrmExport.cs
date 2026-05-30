using System;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using SteamInputBridge.Settings;
using SteamInputBridge.Settings.Profiles;
using SteamInputBridge.Steam;

namespace SteamInputBridge.App;

internal static class SrmExport
{
    public static SrmExportResult Export(IServiceProvider services, string? manifestPathOverride = null)
    {
        try
        {
            ProfilesService profiles = services.GetRequiredService<ProfilesService>();
            ApplicationSettingsService settings =
                services.GetRequiredService<ApplicationSettingsService>();
            SettingsFile settingsFile = services.GetRequiredService<SettingsFile>();

            string manifestPath = ResolveManifestPath(
                manifestPathOverride ?? settings.Current.Steam.SrmExportPath,
                settingsFile.Path);

            string shortcutPath = Path.Combine(
                AppContext.BaseDirectory,
                "SteamInputBridge.exe");
            string manifest = SteamRomManagerExport.CreateJson(profiles, shortcutPath);

            string? directory = Path.GetDirectoryName(manifestPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                _ = Directory.CreateDirectory(directory);
            }

            File.WriteAllText(manifestPath, manifest);
            return SrmExportResult.Success(manifestPath, profiles.ListProfileIds().Count);
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
                Path.GetDirectoryName(settingsPath) ?? AppContext.BaseDirectory,
                filePath);
    }
}

internal sealed record SrmExportResult(
    bool Exported,
    string? ManifestPath,
    int ProfileCount,
    string? Error)
{
    public static SrmExportResult Success(string manifestPath, int profileCount)
    {
        return new SrmExportResult(true, manifestPath, profileCount, null);
    }

    public static SrmExportResult Failure(string error)
    {
        return new SrmExportResult(false, null, 0, error);
    }
}
