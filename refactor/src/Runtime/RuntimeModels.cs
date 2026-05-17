using System;
using System.Collections.Generic;

namespace VirtualMouse.Runtime;

/// <summary>Process observed by a client.</summary>
public sealed record ObservedGameProcess(int ProcessId, string ProcessName);

/// <summary>Describes an active-client transition.</summary>
public sealed class ActiveClientChangedEventArgs(
    Guid? previousClientId,
    Guid? currentClientId) : EventArgs
{
    /// <summary>Previously active client id.</summary>
    public Guid? PreviousClientId { get; } = previousClientId;

    /// <summary>New active client id.</summary>
    public Guid? CurrentClientId { get; } = currentClientId;
}

/// <summary>Runtime status for one client.</summary>
public sealed record ClientStatus(
    Guid ClientId,
    int ClientProcessId,
    string ProfileId,
    bool IsActive,
    IReadOnlyList<ObservedGameProcess> ObservedProcesses,
    IReadOnlyList<ObservedGameProcess> OwnedProcesses,
    IReadOnlyList<ObservedGameProcess> BlockedProcesses);

/// <summary>Current owner and observers for one receiver process.</summary>
public sealed record ReceiverProcessClaimStatus(
    int ProcessId,
    string ProcessName,
    Guid OwnerClientId,
    IReadOnlyList<Guid> ObserverClientIds);

/// <summary>Status for active-client state.</summary>
public sealed record ActiveClientRegistryStatus(
    Guid? ActiveClientId,
    IReadOnlyList<ClientStatus> Clients,
    IReadOnlyList<ReceiverProcessClaimStatus> ReceiverProcesses);
