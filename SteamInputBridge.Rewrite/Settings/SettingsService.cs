using System;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SteamInputBridge.Diagnostics;

namespace SteamInputBridge.Settings;

/// <summary>Validated, re-loadable application settings.</summary>
public sealed class SettingsService : IDisposable
{
    private readonly ILogger<SettingsService> _logger;
    private readonly IDisposable? _reloadSubscription;
    private SteamInputBridgeSettings _current;

    /// <summary>Creates a settings service from re-loadable options.</summary>
    public SettingsService(
        IOptionsMonitor<SteamInputBridgeSettings> settings,
        SettingsFile settingsFile,
        ILogger<SettingsService> logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(settingsFile);
        ArgumentNullException.ThrowIfNull(logger);

        SteamInputBridgeSettings current = settings.CurrentValue;
        SettingsValidation.Validate(current);

        _logger = logger;
        _current = current;
        _reloadSubscription = settings.OnChange((changedSettings, _) => OnSettingsChanged(changedSettings));
        BridgeLog.SettingsLoaded(_logger, settingsFile.Path);
    }

    /// <summary>Raised after a valid settings reload is accepted.</summary>
    public event EventHandler<ApplicationSettingsChangedEventArgs>? Changed;

    /// <summary>Current validated settings snapshot.</summary>
    public SteamInputBridgeSettings Current => Volatile.Read(ref _current);

    /// <summary>Stops listening for settings reloads.</summary>
    public void Dispose()
    {
        _reloadSubscription?.Dispose();
    }

    private void OnSettingsChanged(SteamInputBridgeSettings settings)
    {
        if (!SettingsValidation.TryValidate(settings, out string validationErrors))
        {
            BridgeLog.SettingsReloadRejected(_logger, validationErrors);
            return;
        }

        Volatile.Write(ref _current, settings);
        BridgeLog.SettingsReloaded(_logger, settings);
        Changed?.Invoke(this, new ApplicationSettingsChangedEventArgs(settings));
    }
}

/// <summary>Application settings reload event data.</summary>
public sealed class ApplicationSettingsChangedEventArgs(SteamInputBridgeSettings settings) : EventArgs
{
    /// <summary>Application settings after reload.</summary>
    public SteamInputBridgeSettings Settings { get; } = settings;
}

/// <summary>Application settings file metadata.</summary>
public sealed record SettingsFile(string Path);
