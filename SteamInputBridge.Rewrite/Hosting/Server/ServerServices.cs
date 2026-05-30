using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SteamInputBridge.Settings;

namespace SteamInputBridge.Hosting.Server;

/// <summary>Dependency injection registration for the bridge server.</summary>
public static class ServerServices
{
    /// <summary>Adds server runtime services.</summary>
    public static IServiceCollection AddBridgeServer(
        this IServiceCollection services,
        IConfiguration configuration,
        string settingsPath)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);

        _ = services.AddApplicationSettings(configuration, settingsPath);
        _ = services.AddHostedService<BridgeServer>();

        return services;
    }
}
