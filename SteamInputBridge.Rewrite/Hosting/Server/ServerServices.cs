using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SteamInputBridge.Microphone;
using SteamInputBridge.Profiles;
using SteamInputBridge.Settings;
using SteamInputBridge.Shortcuts;

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
        _ = services.AddSingleton(_ => new MicrophoneService());
        _ = services.AddSingleton<GlobalShortcutListener>();
        _ = services.AddSingleton<ShortcutService>();
        _ = services.AddHostedService(static services => services.GetRequiredService<ShortcutService>());
        _ = services.AddSingleton(static services => new ActionColorService(services.GetRequiredService<ShortcutService>()));
        _ = services.AddSingleton<ProfilesService>();
        _ = services.AddHostedService(static services => services.GetRequiredService<ProfilesService>());
        _ = services.AddSingleton<BridgeService>();
        _ = services.AddHostedService<BridgeServer>();

        return services;
    }
}
