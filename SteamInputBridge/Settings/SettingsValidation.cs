using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using SteamInputBridge.Settings.Profiles;

namespace SteamInputBridge.Settings;

internal static class SettingsValidation
{
    public static void Validate(SteamInputBridgeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        List<string> failures = [];
        ValidateViiper(settings.Viiper, failures);
        ValidateHidHide(settings.HidHide, failures);
        ValidateShortcuts(settings.Shortcuts, failures);
        ValidateProfiles(settings.Games, failures);

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, failures));
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

    private static void ValidateHidHide(HidHideSettings settings, List<string> failures)
    {
        if (!settings.Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(settings.CliPath))
        {
            failures.Add("hidhide:cliPath is required.");
        }
    }

    private static void ValidateShortcuts(
        Collection<ShortcutEntry> shortcuts,
        List<string> failures)
    {
        HashSet<string> targetsByKey = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < shortcuts.Count; i++)
        {
            ShortcutEntry shortcut = shortcuts[i];
            string prefix = $"shortcuts:{i}";
            string? keys = null;
            if (string.IsNullOrWhiteSpace(shortcut.Keys))
            {
                failures.Add($"{prefix}:keys is required.");
            }
            else
            {
                keys = shortcut.Keys.Trim();
            }

            if (shortcut.Targets.Count == 0)
            {
                failures.Add($"{prefix}:targets is required.");
            }

            HashSet<ShortcutTarget> entryTargets = [];
            foreach (ShortcutTarget target in shortcut.Targets)
            {
                if (!entryTargets.Add(target))
                {
                    failures.Add($"{prefix}:targets duplicates {target}.");
                }
                else if (keys is not null && !targetsByKey.Add($"{keys}\0{target}"))
                {
                    failures.Add($"{prefix}:targets duplicates another shortcut target for the same keys.");
                }
            }

            if (!shortcut.Value.HasValue)
            {
                failures.Add($"{prefix}:value is required.");
            }
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

            bool hasExecutable = !string.IsNullOrWhiteSpace(profile.Executable);
            bool hasReceivers = profile.ReceiverProcesses.Any(static receiver =>
                !string.IsNullOrWhiteSpace(receiver));
            if (!hasExecutable && !hasReceivers)
            {
                failures.Add(
                    $"games:{profileId}:receiverProcesses is required when executable is missing.");
            }
        }
    }
}
