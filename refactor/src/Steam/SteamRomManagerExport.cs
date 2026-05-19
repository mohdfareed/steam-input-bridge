using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using VirtualMouse.Settings.Profiles;

namespace VirtualMouse.Steam;

/// <summary>Writes Steam ROM Manager entries for configured game profiles.</summary>
public static class SteamRomManagerExport
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    /// <summary>Creates the Steam ROM Manager manifest JSON.</summary>
    /// <param name="profiles">Profile lookup.</param>
    /// <param name="appPath">Virtual Mouse executable used as the shortcut target.</param>
    public static string CreateJson(ProfilesService profiles, string appPath)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentException.ThrowIfNullOrWhiteSpace(appPath);

        string startIn = Path.GetDirectoryName(appPath) ?? string.Empty;
        List<SteamRomManagerEntry> entries = [];

        foreach (string profileId in profiles.ListProfileIds())
        {
            GameProfile profile = profiles.GetProfile(profileId) ??
                throw new InvalidOperationException($"Profile \"{profileId}\" was not found.");

            ResolvedGameProfile resolved = ProfileResolver.Resolve(profileId, profile);
            entries.Add(new SteamRomManagerEntry(
                resolved.Title,
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
