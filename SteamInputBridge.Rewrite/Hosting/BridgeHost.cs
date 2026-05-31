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
    public static IHost CreateSettings(string basePath, Action<ILoggingBuilder, ConfigurationManager> configureLogging)
    {
        HostApplicationBuilder builder = CreateBuilder(basePath, configureLogging, out string settingsPath);
        _ = builder.Services.AddApplicationSettings(builder.Configuration, settingsPath);
        return builder.Build();
    }

    /// <summary>Creates a server host.</summary>
    public static IHost CreateServer(string basePath, Action<ILoggingBuilder, ConfigurationManager> configureLogging)
    {
        HostApplicationBuilder builder = CreateBuilder(basePath, configureLogging, out string settingsPath);
        _ = builder.Services.AddBridgeServer(builder.Configuration, settingsPath);
        return builder.Build();
    }

    /// <summary>Creates a client host.</summary>
    public static IHost CreateClient(string basePath, string profileId, Action<ILoggingBuilder, ConfigurationManager> configureLogging)
    {
        HostApplicationBuilder builder = CreateBuilder(basePath, configureLogging, out _);
        _ = builder.Services.AddBridgeClient(profileId);
        return builder.Build();
    }

    // MARK: Implementation
    // ========================================================================

    private static HostApplicationBuilder CreateBuilder(
        string basePath,
        Action<ILoggingBuilder, ConfigurationManager> configureLogging,
        out string settingsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(basePath);
        ArgumentNullException.ThrowIfNull(configureLogging);

        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        settingsPath = Path.Combine(basePath, "appsettings.json");

        _ = builder.Configuration
            .SetBasePath(basePath)
            .AddJsonFile(settingsPath, optional: true, reloadOnChange: true);

        configureLogging(builder.Logging, builder.Configuration);

        return builder;
    }
}
