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
