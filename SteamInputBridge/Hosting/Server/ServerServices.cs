using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SteamInputBridge.Forwarding.Controller;
using SteamInputBridge.Forwarding.Mouse;
using SteamInputBridge.Inputs.Mouse;
using SteamInputBridge.Inputs.RawInput;
using SteamInputBridge.Microphone;
using SteamInputBridge.Outputs.Mouse;
using SteamInputBridge.Outputs.Teensy;
using SteamInputBridge.Outputs.Viiper.Mouse;
using SteamInputBridge.Profiles;
using SteamInputBridge.Settings;
using SteamInputBridge.Shortcuts;
using SteamInputBridge.Steam;

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

        // Configuration
        _ = services.AddApplicationSettings(configuration, settingsPath);

        // Profile and client runtime state
        _ = services.AddSingleton<ProfileCatalogService>();
        _ = services.AddHostedService(static services => services.GetRequiredService<ProfileCatalogService>());
        _ = services.AddSingleton<ProfileClientsService>();
        _ = services.AddSingleton<ActiveProfileService>();
        _ = services.AddHostedService(static services => services.GetRequiredService<ActiveProfileService>());

        // Shortcuts and shortcut-driven features
        _ = services.AddSingleton<GlobalShortcutListener>();
        _ = services.AddSingleton<ShortcutService>();
        _ = services.AddHostedService(static services => services.GetRequiredService<ShortcutService>());
        _ = services.AddSingleton(static services => new ActionColorService(services.GetRequiredService<ShortcutService>()));

        // Microphone
        _ = services.AddSingleton(_ => new MicrophoneService());
        _ = services.AddSingleton<MicrophoneShortcutService>();
        _ = services.AddHostedService(static services => services.GetRequiredService<MicrophoneShortcutService>());

        // Mouse forwarding
        _ = services.AddSingleton<IMouseInputSourceFactory, RawInputMouseSourceFactory>();
        _ = services.AddSingleton<ViiperMouseOutputFactory>();
        _ = services.AddSingleton<TeensyMouseOutputFactory>();
        _ = services.AddSingleton<IMouseOutputFactory, MouseOutputFactory>();
        _ = services.AddSingleton<ServerMouseForwardingService>();
        _ = services.AddHostedService(static services => services.GetRequiredService<ServerMouseForwardingService>());

        // Active-profile side effects
        _ = services.AddSingleton<SteamInputConfigService>();
        _ = services.AddHostedService(static services => services.GetRequiredService<SteamInputConfigService>());
        _ = services.AddSingleton<ServerControllerForwardingService>();
        _ = services.AddHostedService(static services => services.GetRequiredService<ServerControllerForwardingService>());

        // Control server
        _ = services.AddSingleton<BridgeService>();
        _ = services.AddHostedService<BridgeServer>();

        return services;
    }
}
