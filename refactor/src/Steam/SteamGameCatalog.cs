using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace VirtualMouse.Steam;

internal sealed class SteamGameCatalog(string steamPath)
{
    private readonly string _steamPath = string.IsNullOrWhiteSpace(steamPath)
        ? throw new ArgumentException("Steam path is required.", nameof(steamPath))
        : Path.GetFullPath(steamPath);

    // MARK: API
    // ========================================================================

    public IReadOnlyList<SteamGame> ListGames(uint? steamUserId = null)
    {
        List<SteamGame> games = [.. ReadSteamApps()];
        if (steamUserId.HasValue)
        {
            games.AddRange(ReadNonSteamShortcuts(steamUserId.Value));
        }

        return
        [
            .. games
                .OrderBy(game => game.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(game => game.AppId),
        ];
    }

    // MARK: Helpers
    // ========================================================================

    private IReadOnlyList<string> ReadLibraryFolders()
    {
        Dictionary<string, string> libraries = new(StringComparer.OrdinalIgnoreCase)
        {
            [_steamPath] = _steamPath,
        };

        string libraryFoldersPath = Path.Combine(_steamPath, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(libraryFoldersPath))
        {
            return [.. libraries.Values];
        }

        SteamKeyValue root = SteamKeyValueParser.ParseText(File.ReadAllText(libraryFoldersPath));
        SteamKeyValue libraryFolders = root.GetChild("libraryfolders") ?? root;

        foreach (SteamKeyValue child in libraryFolders.Children.Values)
        {
            string? path = child.Value ?? child.GetValue("path");
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            string fullPath = Path.GetFullPath(path);
            _ = libraries.TryAdd(fullPath, fullPath);
        }

        return [.. libraries.Values];
    }

    private List<SteamGame> ReadSteamApps()
    {
        List<SteamGame> games = [];
        foreach (string libraryPath in ReadLibraryFolders())
        {
            string steamAppsPath = Path.Combine(libraryPath, "steamapps");
            if (!Directory.Exists(steamAppsPath))
            {
                continue;
            }

            foreach (string manifestPath in Directory.EnumerateFiles(steamAppsPath, "appmanifest_*.acf"))
            {
                SteamGame? game = ReadSteamAppManifest(libraryPath, manifestPath);
                if (game is not null)
                {
                    games.Add(game);
                }
            }
        }

        return games;
    }

    private List<SteamGame> ReadNonSteamShortcuts(uint steamUserId)
    {
        string userId = steamUserId.ToString(CultureInfo.InvariantCulture);
        string shortcutsPath = Path.Combine(steamPath, "userdata", userId, "config", "shortcuts.vdf");
        if (!File.Exists(shortcutsPath))
        {
            return [];
        }

        SteamKeyValue root = SteamKeyValueParser.ParseBinary(File.ReadAllBytes(shortcutsPath));
        SteamKeyValue shortcuts = root.GetChild("shortcuts") ?? root;

        List<SteamGame> games = [];
        foreach (SteamKeyValue shortcut in shortcuts.Children.Values)
        {
            SteamGame? game = CreateNonSteamShortcutGame(shortcut);
            if (game is not null)
            {
                games.Add(game);
            }
        }

        return games;
    }

    private static SteamGame? ReadSteamAppManifest(string libraryPath, string manifestPath)
    {
        SteamKeyValue root = SteamKeyValueParser.ParseText(File.ReadAllText(manifestPath));
        SteamKeyValue appState = root.GetChild("AppState") ?? root;
        if (!TryGetUInt32(appState.GetValue("appid"), out uint appId))
        {
            return null;
        }

        string name = appState.GetValue("name") ?? appId.ToString(CultureInfo.InvariantCulture);
        string? installDirectoryName = appState.GetValue("installdir");
        string? localPath = string.IsNullOrWhiteSpace(installDirectoryName)
            ? null
            : Path.Combine(libraryPath, "steamapps", "common", installDirectoryName);

        return new SteamGame
        {
            AppId = appId,
            Name = name,
            Kind = SteamGameKind.SteamApp,
            LocalPath = localPath,
        };
    }

    private static SteamGame? CreateNonSteamShortcutGame(SteamKeyValue shortcut)
    {
        string? _id = shortcut.GetValue("appid");
        string? _dir = shortcut.GetValue("StartDir");
        string? _exe = shortcut.GetValue("Exe");
        string? name = shortcut.GetValue("AppName");

        if (!TryGetUInt32(_id, out uint appId))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        string? startDirectory = string.IsNullOrWhiteSpace(_dir) ? null : _dir;
        string? executablePath = string.IsNullOrWhiteSpace(_exe) ? null : _exe;

        return new SteamGame
        {
            AppId = appId,
            Name = name,
            Kind = SteamGameKind.NonSteamShortcut,
            LocalPath = startDirectory ?? executablePath,
        };
    }

    private static bool TryGetUInt32(string? value, out uint result)
    {
        if (uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result))
        {
            return true;
        }

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int signed))
        {
            result = unchecked((uint)signed);
            return true;
        }

        result = 0;
        return false;
    }
}

internal static class SteamLocator
{

    // MARK: API
    // ========================================================================

    public static string? FindSteamPath()
    {
        string? steamPath = FirstExistingDirectory(
            Environment.GetEnvironmentVariable("SteamPath"),
            Environment.GetEnvironmentVariable("SteamDir"),
            ReadRegistryString(@"Software\Valve\Steam", "SteamPath"),
            ReadRegistryString(@"Software\Valve\Steam", "InstallPath"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"));

        return steamPath is null ? null : Path.GetFullPath(steamPath);
    }

    public static uint? FindActiveUserId()
    {
        return TryParseUInt32(ReadRegistryString(@"Software\Valve\Steam\ActiveProcess", "ActiveUser"));
    }

    // MARK: Helpers
    // ========================================================================

    private static string? FirstExistingDirectory(params string?[] paths)
    {
        foreach (string? path in paths)
        {
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private static string? ReadRegistryString(string keyName, string valueName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(keyName);
        object? value = key?.GetValue(valueName);
        return value switch
        {
            string text => text,
            int number => number.ToString(CultureInfo.InvariantCulture),
            uint number => number.ToString(CultureInfo.InvariantCulture),
            _ => null,
        };
    }

    private static uint? TryParseUInt32(string? value)
    {
        return uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out uint result)
            ? result
            : null;
    }
}
