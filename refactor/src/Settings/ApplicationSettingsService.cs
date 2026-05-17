using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace VirtualMouse.Settings;

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

        _ = services.AddSingleton(new SettingsFile(settingsPath));
        _ = services.AddSingleton<ApplicationSettingsService>();
        _ = services.Configure<VirtualMouseSettings>(
            configuration.GetSection(VirtualMouseSettings.SectionName));
        _ = services.Configure<HostingSettings>(
            configuration.GetSection(HostingSettings.SectionName));
        _ = services.Configure<GeneralSettings>(
            configuration.GetSection(GeneralSettings.SectionName));
        _ = services.Configure<LoggingSettings>(
            configuration.GetSection(LoggingSettings.SectionName));
        return services;
    }
}

// MARK: Implementation
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
        IOptionsMonitor<VirtualMouseSettings> settings,
        ILogger<ApplicationSettingsService> logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
        Current = settings.CurrentValue;
        LogSettingsLoaded(_logger, null);
        _reloadSubscription = settings.OnChange(OnSettingsChanged);
    }

    /// <summary>Raised after application settings reload.</summary>
    public event EventHandler<ApplicationSettingsChangedEventArgs>? Changed;

    /// <summary>Current application settings snapshot.</summary>
    public VirtualMouseSettings Current { get; private set; }

    /// <summary>Stops listening for settings reloads.</summary>
    public void Dispose()
    {
        _reloadSubscription?.Dispose();
    }

    private void OnSettingsChanged(VirtualMouseSettings settings)
    {
        Current = settings;
        LogSettingsReloaded(_logger, null);
        Changed?.Invoke(this, new ApplicationSettingsChangedEventArgs(settings));
    }
}

/// <summary>Application settings reload event data.</summary>
public sealed class ApplicationSettingsChangedEventArgs(VirtualMouseSettings settings) : EventArgs
{
    /// <summary>Application settings after reload.</summary>
    public VirtualMouseSettings Settings { get; } = settings;
}
