using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SteamInputBridge.Settings;

// MARK: Dependency Injection
// ============================================================================

/// <summary>Dependency injection registration for application settings.</summary>
public static class SettingsServices
{
    /// <summary>Adds application settings services.</summary>
    public static IServiceCollection AddApplicationSettings(
        this IServiceCollection services,
        IConfiguration configuration,
        string settingsPath)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);

        // Singletons
        _ = services.AddSingleton(new SettingsFile(settingsPath));
        _ = services.AddSingleton<ApplicationSettingsService>();

        // Options
        _ = services.Configure<SteamInputBridgeSettings>(
            configuration.GetSection(SteamInputBridgeSettings.SectionName));

        // Injectable options
        _ = services.Configure<ViiperSettings>(
            configuration.GetSection(ViiperSettings.SectionName));
        return services;
    }
}

// MARK: Publics
// ============================================================================

/// <summary>Read-only access to reload-able application settings.</summary>
public sealed class ApplicationSettingsService : IDisposable
{
    private static readonly Action<ILogger, Exception?> LogSettingsLoaded =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(1, nameof(LogSettingsLoaded)),
            "Application settings loaded.");

    private static readonly Action<ILogger, Exception?> LogSettingsReloaded =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(2, nameof(LogSettingsReloaded)),
            "Application settings reloaded.");

    private readonly ILogger<ApplicationSettingsService> _logger;
    private readonly IDisposable? _reloadSubscription;

    /// <summary>Creates a service from reload-able application settings.</summary>
    public ApplicationSettingsService(
        IOptionsMonitor<SteamInputBridgeSettings> settings,
        ILogger<ApplicationSettingsService> logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
        SettingsValidation.Validate(settings.CurrentValue);
        Current = settings.CurrentValue;
        LogSettingsLoaded(_logger, null);
        _reloadSubscription = settings.OnChange(OnSettingsChanged);
    }

    /// <summary>Raised after application settings reload.</summary>
    public event EventHandler<ApplicationSettingsChangedEventArgs>? Changed;

    /// <summary>Current application settings snapshot.</summary>
    public SteamInputBridgeSettings Current { get; private set; }

    /// <summary>Stops listening for settings reloads.</summary>
    public void Dispose()
    {
        _reloadSubscription?.Dispose();
    }

    private void OnSettingsChanged(SteamInputBridgeSettings settings)
    {
        SettingsValidation.Validate(settings);
        Current = settings;
        LogSettingsReloaded(_logger, null);
        Changed?.Invoke(this, new ApplicationSettingsChangedEventArgs(settings));
    }
}

/// <summary>Application settings reload event data.</summary>
public sealed class ApplicationSettingsChangedEventArgs(SteamInputBridgeSettings settings) : EventArgs
{
    /// <summary>Application settings after reload.</summary>
    public SteamInputBridgeSettings Settings { get; } = settings;
}
