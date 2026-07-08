using System;
using System.Collections.Generic;
using SteamInputBridge.Settings;

namespace SteamInputBridge.Profiles;

// MARK: Models
// ============================================================================

/// <summary>Resolved profile settings used by runtime services.</summary>
internal sealed record ResolvedProfile(
    string Id,
    GameProfile Definition);

/// <summary>Resolved profile runtime status.</summary>
public sealed record ProfileStatus(
    string Id,
    GameProfile Definition,
    uint? EffectiveSteamAppId,
    IReadOnlyList<int> GameProcessIds,
    bool Active,
    int? ClientProcessId,
    Guid? ClientConnectionId);

/// <summary>Connected profile client snapshot.</summary>
internal sealed record ProfileClientStatus(
    Guid ConnectionId,
    int ProcessId,
    string ProfileId,
    uint? SteamAppId,
    IReadOnlyList<int> ReceiverProcessIds);

// MARK: Events
// ============================================================================

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
