using System;
using System.Collections.Generic;
using VirtualMouse.Settings.Profiles;

namespace VirtualMouse.Settings;

internal static class SettingsValidation
{
    public static void Validate(VirtualMouseSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        List<string> failures = [];
        ValidateHosting(settings.Hosting, failures);
        ValidateViiper(settings.Viiper, failures);
        ValidateProfiles(settings.Games, failures);

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, failures));
        }
    }

    private static void ValidateHosting(HostingSettings settings, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(settings.PipeName))
        {
            failures.Add("hosting:pipeName is required.");
        }

        if (settings.ReconnectDelayMilliseconds <= 0)
        {
            failures.Add("hosting:reconnectDelayMilliseconds must be greater than zero.");
        }

        if (settings.KeepAliveMilliseconds <= 0)
        {
            failures.Add("hosting:keepAliveMilliseconds must be greater than zero.");
        }

        if (settings.ForegroundPollMilliseconds <= 0)
        {
            failures.Add("hosting:foregroundPollMilliseconds must be greater than zero.");
        }
    }

    private static void ValidateViiper(ViiperSettings settings, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(settings.Host))
        {
            failures.Add("viiper:host is required.");
        }

        if (settings.Port is < 1 or > 65_535)
        {
            failures.Add("viiper:port must be between 1 and 65535.");
        }
    }

    private static void ValidateProfiles(
        IReadOnlyDictionary<string, GameProfile> profiles,
        List<string> failures)
    {
        foreach ((string profileId, GameProfile profile) in profiles)
        {
            if (string.IsNullOrWhiteSpace(profileId))
            {
                failures.Add("games contains an empty profile id.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(profile.Executable))
            {
                failures.Add($"games:{profileId}:executable is required.");
            }
        }
    }
}
