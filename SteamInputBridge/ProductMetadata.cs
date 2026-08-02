using System;
using System.IO;
using System.Reflection;

namespace SteamInputBridge;

/// <summary>Product metadata and shared executable path conventions.</summary>
public static class ProductMetadata
{
    /// <summary>Product name shared through assembly metadata.</summary>
    public static string Name { get; } = typeof(ProductMetadata).Assembly
        .GetCustomAttribute<AssemblyProductAttribute>()?.Product ??
        throw new InvalidOperationException("Assembly product metadata is unavailable.");

    /// <summary>Product display name shared through assembly metadata.</summary>
    public static string DisplayName { get; } = typeof(ProductMetadata).Assembly
        .GetCustomAttribute<AssemblyTitleAttribute>()?.Title ??
        throw new InvalidOperationException("Assembly title metadata is unavailable.");

    /// <summary>App executable file name.</summary>
    public const string AppExecutableName = "SteamInputBridge.App.exe";

    /// <summary>Packaged Teensy firmware artifact file name.</summary>
    public const string TeensyFirmwareFileName = "SteamInputBridge.Teensy.hex";

    /// <summary>App-local Teensy upload tools directory name.</summary>
    public const string TeensyToolsDirectoryName = "teensy";

    /// <summary>Resolves the current user's non-roaming product data directory.</summary>
    public static string ResolveDataDirectory()
    {
        string localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return string.IsNullOrWhiteSpace(localApplicationData)
            ? throw new InvalidOperationException("The current user's local application data directory is unavailable.")
            : Path.Combine(localApplicationData, Name);
    }

    /// <summary>Resolves the app executable beside a base directory.</summary>
    public static string ResolveAppExecutablePath(string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        return Path.Combine(baseDirectory, AppExecutableName);
    }

    /// <summary>Reads the product version from an assembly.</summary>
    public static string Version(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        string? version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ??
            assembly.GetName().Version?.ToString();
        return string.IsNullOrWhiteSpace(version) ? "unknown" : version;
    }
}
