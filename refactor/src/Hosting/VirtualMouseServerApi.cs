using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ProfileControllerOutput = VirtualMouse.Settings.Profiles.ControllerOutput;
using ProfileMouseOutput = VirtualMouse.Settings.Profiles.MouseOutput;
using VirtualMouse.Forwarding;
using PolyType;
using StreamJsonRpc;
using VirtualMouse.Runtime;

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
    ProfileControllerOutput ControllerOutput,
    ProfileMouseOutput MouseOutput,
    string ControllerPipeName);

/// <summary>Client request to start or restore a profile-backed run.</summary>
public sealed record StartRunRequest(string ProfileId, uint? SteamAppId);

/// <summary>Client-visible controller mapped to one physical controller slot.</summary>
public sealed record ClientControllerInfo(
    ushort ControllerIndex,
    string PhysicalControllerId,
    ControllerFeatures Features);

// MARK: API
// ============================================================================

/// <summary>JSON-RPC contract between app clients and the local server.</summary>
[JsonRpcContract]
[GenerateShape(IncludeMethods = MethodShapeFlags.PublicInstance)]
internal partial interface IVirtualMouseServerApi
{
    /// <summary>Registers this pipe as one connected client process.</summary>
    Task<Guid> ConnectAsync(int processId);

    /// <summary>Checks that the server pipe is still responsive.</summary>
    Task AckAsync();

    /// <summary>Gets server, client, and active-client status.</summary>
    Task<ServerStatus> GetStatusAsync();

    /// <summary>Starts or restores this client's active profile run.</summary>
    Task<ClientRunLaunch> StartRunAsync(StartRunRequest request);

    /// <summary>Registers controllers this client can stream over its controller pipe.</summary>
    Task RegisterClientControllersAsync(IReadOnlyList<ClientControllerInfo> controllers);

    /// <summary>Updates the receiver processes currently observed by this client.</summary>
    Task UpdateRunProcessesAsync(IReadOnlyList<ObservedGameProcess> processes);

    /// <summary>Gets receiver processes currently owned by this client.</summary>
    Task<IReadOnlyList<ObservedGameProcess>> GetOwnedReceiverProcessesAsync();

    /// <summary>Ends this client's active profile run.</summary>
    Task EndRunAsync();
}
