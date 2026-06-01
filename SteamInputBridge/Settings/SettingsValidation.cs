using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using SteamInputBridge.Shortcuts.Runtime;

namespace SteamInputBridge.Settings;

/// <summary>Validates application settings after configuration binding.</summary>
public static class SettingsValidation
{
    // MARK: Publics
    // ========================================================================

    /// <summary>Throws when the supplied settings are invalid.</summary>
    public static void Validate(SteamInputBridgeSettings settings)
    {
        if (!TryValidate(settings, out string validationErrors))
        {
            throw new InvalidOperationException(validationErrors);
        }
    }

    /// <summary>Validates settings and returns formatted validation failures.</summary>
    public static bool TryValidate(SteamInputBridgeSettings? settings, out string validationErrors)
    {
        List<string> failures = [];
        if (settings is null)
        {
            failures.Add("settings are required.");
        }
        else
        {
            ValidateViiper(settings.Viiper, failures);
            ValidateTeensy(settings.Teensy, failures);
            ValidateShortcuts(settings.Shortcuts, failures);
            ValidateProfiles(settings.Games, failures);
        }

        validationErrors = string.Join(Environment.NewLine, failures);
        return failures.Count == 0;
    }

    // MARK: Implementation
    // ========================================================================

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

    private static void ValidateTeensy(TeensySettings settings, List<string> failures)
    {
        if (!string.IsNullOrWhiteSpace(settings.Port) && !IsComPortName(settings.Port.Trim()))
        {
            failures.Add("teensy:port must be a COM port name such as COM5.");
        }
    }

    private static void ValidateShortcuts(Collection<ShortcutEntry> shortcuts, List<string> failures)
    {
        foreach (ShortcutEntry shortcut in shortcuts)
        {
            if (string.IsNullOrWhiteSpace(shortcut.Keys))
            {
                failures.Add("shortcuts:keys is required.");
                continue;
            }

            string prefix = $"shortcuts:{shortcut.Keys.Trim()}";
            try
            {
                _ = KeyboardShortcutParser.Parse(shortcut.Keys.Trim());
            }
            catch (FormatException exception)
            {
                failures.Add($"{prefix}:keys is invalid: {exception.Message}");
            }

            if (shortcut.Targets.Count == 0)
            {
                failures.Add($"{prefix}:targets is required.");
            }

            if (!Enum.IsDefined(shortcut.Action))
            {
                failures.Add($"{prefix}:action is invalid.");
            }
        }
    }

    private static void ValidateProfiles(IReadOnlyDictionary<string, GameProfile> profiles, List<string> failures)
    {
        foreach ((string profileId, GameProfile profile) in profiles)
        {
            if (string.IsNullOrWhiteSpace(profileId))
            {
                failures.Add("games contains an empty profile id.");
                continue;
            }

            bool hasExecutable = !string.IsNullOrWhiteSpace(profile.Executable);
            bool hasReceivers = profile.ReceiverProcesses.Any(
                static receiver => !string.IsNullOrWhiteSpace(receiver));

            if (!hasExecutable && !hasReceivers)
            {
                failures.Add($"games:{profileId}:receiverProcesses is required when executable is missing.");
            }

            if (profile.ReceiverProcesses.Any(string.IsNullOrWhiteSpace))
            {
                failures.Add($"games:{profileId}:receiverProcesses cannot contain empty values.");
            }

            if (profile.ControllerOutput.HasValue && !Enum.IsDefined(profile.ControllerOutput.Value))
            {
                failures.Add($"games:{profileId}:controllerOutput is invalid.");
            }

            if (profile.MouseOutput.HasValue && !Enum.IsDefined(profile.MouseOutput.Value))
            {
                failures.Add($"games:{profileId}:mouseOutput is invalid.");
            }
        }
    }

    // MARK: Helpers
    // ========================================================================

    private static bool IsComPortName(string value)
    {
        if (!value.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || value.Length == 3)
        {
            return false;
        }

        for (int i = 3; i < value.Length; i++)
        {
            if (!char.IsAsciiDigit(value[i]))
            {
                return false;
            }
        }

        return int.TryParse(value[3..], out int number) && number > 0;
    }
}
