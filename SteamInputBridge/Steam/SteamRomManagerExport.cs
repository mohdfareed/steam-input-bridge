using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using SteamInputBridge.Settings;

namespace SteamInputBridge.Steam;

/// <summary>Writes Steam ROM Manager entries for configured game profiles.</summary>
public static class SteamRomManagerExport
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    // MARK: Publics
    // ========================================================================

    /// <summary>Creates the Steam ROM Manager manifest JSON.</summary>
    /// <param name="profiles">Configured game profiles by profile id.</param>
    /// <param name="appPath">Steam Input Bridge executable used as the shortcut target.</param>
    public static string CreateJson(IReadOnlyDictionary<string, GameProfile> profiles, string appPath)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentException.ThrowIfNullOrWhiteSpace(appPath);

        string startIn = Path.GetDirectoryName(appPath) ?? string.Empty;
        List<SteamRomManagerEntry> entries = [];

        foreach ((string profileId, GameProfile profile) in profiles)
        {
            entries.Add(new SteamRomManagerEntry(
                profile.Title,
                appPath,
                startIn,
                $"shortcut {QuoteArgument(profileId)}",
                AppendArgsToExecutable: false));
        }

        return JsonSerializer.Serialize(entries, JsonOptions);
    }

    /// <summary>Writes a Steam ROM Manager manifest from the current settings service.</summary>
    /// <param name="settings">Settings service.</param>
    /// <param name="settingsFile">Settings file path value.</param>
    /// <param name="appPath">Steam Input Bridge executable used as the shortcut target.</param>
    /// <param name="manifestPathOverride">Optional manifest path override.</param>
    public static string WriteManifest(
        SettingsService settings,
        SettingsFile settingsFile,
        string appPath,
        string? manifestPathOverride = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(settingsFile);
        ArgumentException.ThrowIfNullOrWhiteSpace(appPath);

        string? path = manifestPathOverride ?? settings.Current.Steam.SrmExportPath;
        string manifestPath = ResolveManifestPath(path, settingsFile.Path);
        string manifest = CreateJson(settings.Current.Games, appPath);

        string? directory = Path.GetDirectoryName(manifestPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            _ = Directory.CreateDirectory(directory);
        }

        File.WriteAllText(manifestPath, manifest);
        return manifestPath;
    }

    // MARK: Implementation
    // ========================================================================

    private static string ResolveManifestPath(string? path, string settingsPath)
    {
        string configuredPath = string.IsNullOrWhiteSpace(path) ? "srm-manifest.json" : path;
        string filePath = Environment.ExpandEnvironmentVariables(configuredPath);
        return Path.IsPathFullyQualified(filePath)
            ? filePath
            : Path.Combine(Path.GetDirectoryName(settingsPath) ?? AppContext.BaseDirectory, filePath);
    }

    private static string QuoteArgument(string value)
    {
        return !value.Contains(' ', StringComparison.Ordinal) && !value.Contains('"', StringComparison.Ordinal)
            ? value
            : $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }

    private sealed record SteamRomManagerEntry(
        string Title,
        string Target,
        string StartIn,
        string LaunchOptions,
        bool AppendArgsToExecutable);
}
