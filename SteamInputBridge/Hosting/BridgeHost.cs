using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Hosting.Client;
using SteamInputBridge.Hosting.Server;
using SteamInputBridge.Settings;

namespace SteamInputBridge.Hosting;

/// <summary>Builds product hosts with shared settings loading.</summary>
public static class BridgeHost
{
    // MARK: Publics
    // ========================================================================

    /// <summary>Creates a host with settings services only.</summary>
    public static IHost CreateSettings(Action<ILoggingBuilder, ConfigurationManager, SettingsFile> configureLogging)
    {
        HostApplicationBuilder builder = CreateBuilder(configureLogging, out SettingsFile settingsFile);
        _ = builder.Services.AddApplicationSettings(builder.Configuration, settingsFile);
        return builder.Build();
    }

    /// <summary>Creates a server host.</summary>
    public static IHost CreateServer(Action<ILoggingBuilder, ConfigurationManager, SettingsFile> configureLogging)
    {
        HostApplicationBuilder builder = CreateBuilder(configureLogging, out SettingsFile settingsFile);
        _ = builder.Services.AddBridgeServer(builder.Configuration, settingsFile);
        return builder.Build();
    }

    /// <summary>Creates a client host.</summary>
    public static IHost CreateClient(
        string profileId,
        Action<ILoggingBuilder, ConfigurationManager, SettingsFile> configureLogging)
    {
        HostApplicationBuilder builder = CreateBuilder(configureLogging, out SettingsFile settingsFile);
        _ = builder.Services.AddApplicationSettings(builder.Configuration, settingsFile);
        _ = builder.Services.AddBridgeClient(profileId);
        return builder.Build();
    }

    // MARK: Implementation
    // ========================================================================

    private static HostApplicationBuilder CreateBuilder(
        Action<ILoggingBuilder, ConfigurationManager, SettingsFile> configureLogging,
        out SettingsFile settingsFile)
    {
        ArgumentNullException.ThrowIfNull(configureLogging);

        settingsFile = ResolveSettingsFile(
            Environment.CurrentDirectory,
            AppContext.BaseDirectory,
            ProductMetadata.ResolveDataDirectory());
        _ = Directory.CreateDirectory(settingsFile.DirectoryPath);

        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = settingsFile.DirectoryPath,
        });
        configureLogging(builder.Logging, builder.Configuration, settingsFile);

        return builder;
    }

    internal static SettingsFile ResolveSettingsFile(
        string currentDirectory,
        string baseDirectory,
        string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);

        const string fileName = "appsettings.json";
        string[] candidates =
        [
            Path.Combine(Path.GetFullPath(currentDirectory), fileName),
            Path.Combine(Path.GetFullPath(baseDirectory), fileName),
            Path.Combine(Path.GetFullPath(dataDirectory), fileName),
        ];

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return new(candidate);
            }
        }

        return new(candidates[^1]);
    }
}
