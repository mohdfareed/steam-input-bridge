using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PolyType;
using StreamJsonRpc;
using VirtualMouse.Runtime;
using VirtualMouse.Settings.Profiles;

namespace VirtualMouse.Hosting;

// MARK: Models
// ============================================================================

/// <summary>Resolved launch details for a profile-backed client run.</summary>
public sealed record ClientRunLaunch(
    string ProfileId,
    string Title,
    string Executable,
    string Arguments,
    string WorkingDirectory,
    IReadOnlyList<string> ReceiverProcesses,
    ControllerOutput ControllerOutput,
    MouseOutput MouseOutput);

// MARK: API
// ============================================================================

[JsonRpcContract]
[GenerateShape(IncludeMethods = MethodShapeFlags.PublicInstance)]
internal partial interface IVirtualMouseServerApi
{
    Task<Guid> ConnectAsync(int processId);

    Task AckAsync();

    Task<ServerStatus> GetStatusAsync();

    Task<ClientRunLaunch> StartRunAsync(string profileId);

    Task UpdateRunProcessesAsync(IReadOnlyList<ObservedGameProcess> processes);

    Task<IReadOnlyList<ObservedGameProcess>> GetOwnedReceiverProcessesAsync();

    Task EndRunAsync();
}
