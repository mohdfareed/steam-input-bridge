using System;
using System.Collections.Generic;
using SteamInputBridge.Forwarding.Controller;
using SteamInputBridge.Forwarding.Controller.Routing;
using SteamInputBridge.Forwarding.Mouse;
using SteamInputBridge.Runtime;

namespace SteamInputBridge.Hosting.Server.Orchestration;

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
        new(
            null,
            MouseOutputEnabled: true,
            PointerOutputEnabled: true,
            OutputConnected: false,
            MouseOutput.None,
            []);

    /// <summary>Server-owned input pump status.</summary>
    public ServerInputStatus Inputs { get; init; } =
        new(new MouseInputPumpStatus(false, false, null));

    /// <summary>HidHide scope currently applied by this server.</summary>
    public ServerHidHideStatus HidHide { get; init; } =
        new(false, false, false, [], [], [], null, null);

    /// <summary>Steam Input forcing status tracked by this server.</summary>
    public ServerSteamInputStatus SteamInput { get; init; } =
        new(false, null, null, null);

    /// <summary>Connected controller stream pipe status.</summary>
    public IReadOnlyList<ControllerPipeStatus> ControllerPipes { get; init; } = [];

    /// <summary>Tray overlay indicator status.</summary>
    public OverlayStatus Overlay { get; init; } = OverlayStatus.Hidden;
}

/// <summary>Tray overlay indicator status.</summary>
public sealed record OverlayStatus(MicrophoneOverlayStatus Microphone, string? ActionColor)
{
    /// <summary>Hidden overlay state.</summary>
    public static OverlayStatus Hidden { get; } = new(MicrophoneOverlayStatus.Unavailable, null);
}

/// <summary>Microphone indicator status.</summary>
public sealed record MicrophoneOverlayStatus(
    bool Available,
    bool Muted,
    bool ActivityReliable,
    bool InputActive)
{
    /// <summary>No usable microphone state.</summary>
    public static MicrophoneOverlayStatus Unavailable { get; } =
        new(Available: false, Muted: false, ActivityReliable: false, InputActive: false);
}

/// <summary>Server-owned input source status.</summary>
public sealed record ServerInputStatus(
    MouseInputPumpStatus Mouse,
    ControllerInputPumpStatus Controller = default);

/// <summary>Raw Input mouse pump status.</summary>
public sealed record MouseInputPumpStatus(bool Running, bool SourceConnected, string? LastError);

/// <summary>Physical SDL controller pump status.</summary>
public readonly record struct ControllerInputPumpStatus(bool Running, int SourceCount, string? LastError);

/// <summary>Steam Input configuration currently forced by the server.</summary>
public sealed record ServerSteamInputStatus(
    bool Forced,
    uint? AppId,
    Guid? ClientId,
    string? LastError);

/// <summary>Current HidHide activation status.</summary>
public sealed record ServerHidHideStatus(
    bool Active,
    bool CloakEnabled,
    bool InverseEnabled,
    IReadOnlyList<string> HiddenDevices,
    IReadOnlyList<string> HiddenDeviceLabels,
    IReadOnlyList<string> RegisteredApplications,
    Guid? ClientId,
    string? LastError)
{
    /// <summary>Current number of hidden devices.</summary>
    public int DeviceCount => HiddenDevices.Count;

    /// <summary>Current number of registered applications.</summary>
    public int ApplicationCount => RegisteredApplications.Count;
}

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
    string Label,
    ControllerFeatures Features,
    string? PhysicalDeviceId)
{
    /// <summary>Number of input frames received for this controller route.</summary>
    public long InputFrameCount { get; init; }
}
