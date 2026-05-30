using System;
using System.Globalization;
using System.IO;
using Microsoft.Win32;

namespace SteamInputBridge.Steam.GameCatalog;

internal static class SteamLocator
{
    // MARK: Publics
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

    // MARK: Privates
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
