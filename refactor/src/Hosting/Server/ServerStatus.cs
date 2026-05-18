using System;
using System.Collections.Generic;
using VirtualMouse.Forwarding;
using VirtualMouse.Runtime;

namespace VirtualMouse.Hosting;

/// <summary>Current server status.</summary>
public sealed record ServerStatus(int ConnectedClientCount)
{
    /// <summary>Current active-client runtime status.</summary>
    public ActiveClientRegistryStatus Runtime { get; init; } =
        new(0, null, [], []);

    /// <summary>Current controller forwarding status.</summary>
    public ControllerBrokerStatus Forwarding { get; init; } =
        new(null, ControllerOutputEnabled: true, PhysicalMotionEnabled: true, []);

    /// <summary>Current mouse forwarding status.</summary>
    public MouseBrokerStatus MouseForwarding { get; init; } =
        new(null, MouseOutputEnabled: true, OutputConnected: false, MouseOutput.None);

    /// <summary>Server-owned input pump status.</summary>
    public ServerInputStatus Inputs { get; init; } =
        new(
            new PhysicalControllerPumpStatus(false, 0, [], null),
            new MouseInputPumpStatus(false, false, null));

    /// <summary>Connected controller stream pipe status.</summary>
    public IReadOnlyList<ControllerPipeStatus> ControllerPipes { get; init; } = [];
}

/// <summary>Server-owned input source status.</summary>
public sealed record ServerInputStatus(
    PhysicalControllerPumpStatus PhysicalControllers,
    MouseInputPumpStatus Mouse);

/// <summary>Physical SDL controller pump status.</summary>
public sealed record PhysicalControllerPumpStatus(
    bool Running,
    int ControllerCount,
    IReadOnlyList<string> ControllerIds,
    string? LastError);

/// <summary>Raw Input mouse pump status.</summary>
public sealed record MouseInputPumpStatus(bool Running, bool SourceConnected, string? LastError);

/// <summary>Controller pipe status for one connected client.</summary>
public sealed record ControllerPipeStatus(
    Guid ClientId,
    string PipeName,
    bool Connected,
    IReadOnlyList<ClientControllerStatus> Controllers);

/// <summary>Registered controller stream status.</summary>
public sealed record ClientControllerStatus(
    ushort ControllerIndex,
    string PhysicalControllerId,
    ControllerFeatures Features);
