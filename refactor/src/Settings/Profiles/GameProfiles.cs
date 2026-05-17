using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace VirtualMouse.Settings.Profiles;

// MARK: Game Profiles
// ============================================================================

/// <summary>Virtual controller output selected by a profile.</summary>
public enum ControllerOutput
{
    /// <summary>No virtual controller output.</summary>
    None,

    /// <summary>Xbox 360 virtual controller output.</summary>
    Xbox360,

    /// <summary>DualShock 4 virtual controller output.</summary>
    Ds4,
}

/// <summary>Mouse output selected by a profile.</summary>
public enum MouseOutput
{
    /// <summary>No mouse output.</summary>
    None,

    /// <summary>VIIPER virtual mouse output.</summary>
    Viiper,
}

/// <summary>Configuration for one game profile.</summary>
public sealed class GameProfile
{
    /// <summary>Display title.</summary>
    public string Title { get; set; } = "";

    /// <summary>Executable path.</summary>
    public string Executable { get; set; } = "";

    /// <summary>Optional process arguments.</summary>
    public string? Arguments { get; set; }

    /// <summary>Optional working directory.</summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>Virtual controller output.</summary>
    public ControllerOutput ControllerOutput { get; set; } = ControllerOutput.Xbox360;

    /// <summary>Mouse output.</summary>
    public MouseOutput MouseOutput { get; set; } = MouseOutput.Viiper;

    /// <summary>Processes that identify the receiver game.</summary>
    public Collection<string> ReceiverProcesses { get; } = [];
}

// MARK: Profile Snapshot
// ============================================================================

internal sealed record ProfileSnapshot(
    IReadOnlyDictionary<string, GameProfile> Profiles,
    IReadOnlyList<string> ProfileIds)
{
    public static ProfileSnapshot From(IReadOnlyDictionary<string, GameProfile> settings)
    {
        Validate(settings);
        Dictionary<string, GameProfile> profiles = new(settings, StringComparer.OrdinalIgnoreCase);
        string[] profileIds = [.. profiles.Keys.Order(StringComparer.OrdinalIgnoreCase)];
        return new ProfileSnapshot(profiles, profileIds);
    }

    private static void Validate(IReadOnlyDictionary<string, GameProfile> profiles)
    {
        List<string> failures = [];
        foreach ((string profileId, GameProfile profile) in profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.Title))
            {
                failures.Add($"games:{profileId}:title is required.");
            }

            if (string.IsNullOrWhiteSpace(profile.Executable))
            {
                failures.Add($"games:{profileId}:executable is required.");
            }
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, failures));
        }
    }
}
