using System;
using System.IO;
using System.Reflection;

namespace SteamInputBridge;

/// <summary>Product metadata and shared executable path conventions.</summary>
public static class ProductMetadata
{
    /// <summary>App executable file name.</summary>
    public const string AppExecutableName = "SteamInputBridge.App.exe";

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
