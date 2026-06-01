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
    public static IHost CreateSettings(Action<ILoggingBuilder, ConfigurationManager, IHostEnvironment> configureLogging)
    {
        HostApplicationBuilder builder = CreateBuilder(configureLogging, out string settingsPath);
        _ = builder.Services.AddApplicationSettings(builder.Configuration, settingsPath);
        return builder.Build();
    }

    /// <summary>Creates a server host.</summary>
    public static IHost CreateServer(Action<ILoggingBuilder, ConfigurationManager, IHostEnvironment> configureLogging)
    {
        HostApplicationBuilder builder = CreateBuilder(configureLogging, out string settingsPath);
        _ = builder.Services.AddBridgeServer(builder.Configuration, settingsPath);
        return builder.Build();
    }

    /// <summary>Creates a client host.</summary>
    public static IHost CreateClient(
        string profileId,
        Action<ILoggingBuilder, ConfigurationManager, IHostEnvironment> configureLogging)
    {
        HostApplicationBuilder builder = CreateBuilder(configureLogging, out string settingsPath);
        _ = builder.Services.AddApplicationSettings(builder.Configuration, settingsPath);
        _ = builder.Services.AddBridgeClient(profileId);
        return builder.Build();
    }

    // MARK: Implementation
    // ========================================================================

    private static HostApplicationBuilder CreateBuilder(
        Action<ILoggingBuilder, ConfigurationManager, IHostEnvironment> configureLogging,
        out string settingsPath)
    {
        ArgumentNullException.ThrowIfNull(configureLogging);

        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        settingsPath = Path.Combine(builder.Environment.ContentRootPath, "appsettings.json");

        configureLogging(builder.Logging, builder.Configuration, builder.Environment);

        return builder;
    }
}
