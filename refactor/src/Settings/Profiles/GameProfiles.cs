using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace VirtualMouse.Settings.Profiles;

// MARK: Profiles
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

    /// <summary>Teensy hardware mouse output.</summary>
    Teensy,
}

/// <summary>Configuration for one game profile.</summary>
public sealed class GameProfile
{
    /// <summary>Display title.</summary>
    public string Title { get; set; } = "";

    /// <summary>Optional executable path used to start the game.</summary>
    public string? Executable { get; set; }

    /// <summary>Optional process arguments.</summary>
    public string? Arguments { get; set; }

    /// <summary>Optional Steam app id used when the client cannot read one from Steam.</summary>
    public uint? SteamAppId { get; set; }

    /// <summary>Optional working directory.</summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>Optional virtual controller output.</summary>
    public ControllerOutput? ControllerOutput { get; set; }

    /// <summary>Optional mouse output.</summary>
    public MouseOutput? MouseOutput { get; set; }

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
        Dictionary<string, GameProfile> profiles = new(settings, StringComparer.OrdinalIgnoreCase);
        string[] profileIds = [.. profiles.Keys.Order(StringComparer.OrdinalIgnoreCase)];
        return new ProfileSnapshot(profiles, profileIds);
    }
}
