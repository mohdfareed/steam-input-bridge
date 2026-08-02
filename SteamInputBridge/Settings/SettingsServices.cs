using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SteamInputBridge.Settings;

/// <summary>Dependency injection registration for application settings.</summary>
public static class SettingsServices
{
    /// <summary>Adds settings services backed by the existing SteamInputBridge configuration section.</summary>
    public static IServiceCollection AddApplicationSettings(
        this IServiceCollection services,
        IConfiguration configuration,
        SettingsFile settingsFile)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(settingsFile);

        _ = services.AddSingleton(settingsFile);
        _ = services.AddOptions<SteamInputBridgeSettings>()
            .Bind(configuration.GetSection(SteamInputBridgeSettings.SectionName))
            .Validate(static settings => SettingsValidation.TryValidate(settings, out _), "Settings are invalid.")
            .ValidateOnStart();
        _ = services.AddSingleton<SettingsService>();

        return services;
    }
}
