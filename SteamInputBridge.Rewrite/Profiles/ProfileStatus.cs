using System;
using System.Collections.Generic;
using SteamInputBridge.Outputs;

namespace SteamInputBridge.Profiles;

/// <summary>Resolved profile runtime status.</summary>
public sealed record ProfileStatus(
    string Id,
    string Title,
    uint? ConfiguredSteamAppId,
    uint? EffectiveSteamAppId,
    MouseOutput? MouseOutput,
    ControllerOutput? ControllerOutput,
    IReadOnlyList<string> ReceiverProcesses,
    bool Active,
    int? ClientProcessId);

/// <summary>Profile list change event data.</summary>
public sealed class ProfilesChangedEventArgs(IReadOnlyList<ProfileStatus> profiles) : EventArgs
{
    /// <summary>Current resolved profile statuses.</summary>
    public IReadOnlyList<ProfileStatus> Profiles { get; } = profiles;
}

/// <summary>Active profile change event data.</summary>
public sealed class ActiveProfileChangedEventArgs(ProfileStatus? activeProfile) : EventArgs
{
    /// <summary>Current active profile, or null when no profile is active.</summary>
    public ProfileStatus? ActiveProfile { get; } = activeProfile;
}
