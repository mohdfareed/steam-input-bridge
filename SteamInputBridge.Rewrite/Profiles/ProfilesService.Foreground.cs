using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Steam;
using Vanara.PInvoke;
using static Vanara.PInvoke.User32;

namespace SteamInputBridge.Profiles;

public sealed partial class ProfilesService
{
    private static readonly TimeSpan ForegroundPollInterval = TimeSpan.FromMilliseconds(250);

    private readonly SteamInputClient _steam = new();
    private Task? _monitor;
    private uint? _forcedSteamAppId;

    // MARK: Foreground
    // ========================================================================

    private async Task MonitorForegroundAsync(CancellationToken cancellationToken)
    {
        using PeriodicTimer timer = new(ForegroundPollInterval);
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            ProfileStatus? activeProfile = FindActiveProfile();
            ProfileStatus? previous;
            lock (_gate)
            {
                previous = _activeProfile;
                _activeProfile = activeProfile;
            }

            if (previous?.Id != activeProfile?.Id)
            {
                ActiveProfileChanged?.Invoke(this, new(activeProfile));
                await ApplySteamConfigAsync(activeProfile, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private ProfileStatus? FindActiveProfile()
    {
        string? foregroundProcessName = ForegroundProcessName();
        if (string.IsNullOrWhiteSpace(foregroundProcessName))
        {
            return null;
        }

        foreach (ProfileStatus profile in MonitoredProfiles)
        {
            foreach (string receiverProcess in profile.ReceiverProcesses)
            {
                if (MatchesProcessName(receiverProcess, foregroundProcessName))
                {
                    return profile with { Active = true };
                }
            }
        }

        return null;
    }

    private async Task ApplySteamConfigAsync(ProfileStatus? activeProfile, CancellationToken cancellationToken)
    {
        uint? appId = activeProfile?.EffectiveSteamAppId;
        if (appId == _forcedSteamAppId)
        {
            return;
        }

        try
        {
            await _steam.ForceConfigAsync(appId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            LogSteamConfigFailed(logger, appId, exception.Message, null);
        }
        finally
        {
            _forcedSteamAppId = appId;
        }
    }

    // MARK: Windows
    // ========================================================================

    private static string? ForegroundProcessName()
    {
        HWND foregroundWindow = GetForegroundWindow();
        if (foregroundWindow.IsNull)
        {
            return null;
        }

        _ = GetWindowThreadProcessId(foregroundWindow, out uint processId);
        if (processId == 0)
        {
            return null;
        }

        try
        {
            using Process process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private static bool MatchesProcessName(string configuredProcess, string actualProcess)
    {
        string normalized = configuredProcess.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? configuredProcess[..^4]
            : configuredProcess;

        return string.Equals(normalized, actualProcess, StringComparison.OrdinalIgnoreCase);
    }

    // MARK: Logging
    // ========================================================================

    private static readonly Action<ILogger, uint?, string, Exception?> LogSteamConfigFailed =
        LoggerMessage.Define<uint?, string>(
            LogLevel.Warning,
            new EventId(1, nameof(LogSteamConfigFailed)),
            "Steam Input config update failed for app id {SteamAppId}: {Message}");
}
