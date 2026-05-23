using System;

namespace SteamInputBridge.Forwarding.Controller;

// MARK: Endpoints
// ============================================================================

/// <summary>Receives output feedback for a controller endpoint.</summary>
public interface IControllerFeedbackSink
{
    /// <summary>Attempts to deliver feedback to the endpoint.</summary>
    /// <param name="feedback">Feedback from the game-facing output.</param>
    /// <returns><see langword="true" /> when the endpoint accepted it.</returns>
    bool TrySendFeedback(ControllerFeedback feedback);
}

/// <summary>Game-facing controller output.</summary>
public interface IControllerOutput : IAsyncDisposable
{
    /// <summary>Sends one merged controller state to the output.</summary>
    /// <param name="state">Merged controller state.</param>
    void Send(in ControllerState state);

    /// <summary>Registers for output feedback.</summary>
    /// <param name="handler">Called for each feedback update.</param>
    IDisposable ListenFeedback(Action<ControllerFeedback> handler);
}

/// <summary>Creates game-facing controller outputs.</summary>
public interface IControllerOutputFactory
{
    /// <summary>Connects an output for one physical controller slot.</summary>
    /// <param name="controllerId">Physical controller slot id.</param>
    /// <param name="output">Requested output shape.</param>
    IControllerOutput Connect(ControllerId controllerId, ControllerOutput output);
}

// MARK: Models
// ============================================================================

/// <summary>Stable id and optional display name for one physical controller slot.</summary>
public readonly struct ControllerId : IEquatable<ControllerId>
{
    /// <summary>Creates a controller id.</summary>
    /// <param name="value">Stable controller id.</param>
    /// <param name="displayName">Readable controller name when known.</param>
    public ControllerId(string value, string? displayName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
    }

    /// <summary>Stable controller id.</summary>
    public string Value { get; }

    /// <summary>Readable controller name when known.</summary>
    public string? DisplayName { get; }

    /// <inheritdoc />
    public bool Equals(ControllerId other)
    {
        return string.Equals(Value, other.Value, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is ControllerId other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return StringComparer.Ordinal.GetHashCode(Value);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Value;
    }

    /// <summary>Compares two controller ids by stable value.</summary>
    public static bool operator ==(ControllerId left, ControllerId right)
    {
        return left.Equals(right);
    }

    /// <summary>Compares two controller ids by stable value.</summary>
    public static bool operator !=(ControllerId left, ControllerId right)
    {
        return !left.Equals(right);
    }
}

/// <summary>Stable endpoint id for one controller stream from one client.</summary>
public readonly record struct ControllerEndpointId(Guid ClientId, ushort ControllerIndex);

/// <summary>Game-facing controller output shape.</summary>
public enum ControllerOutput
{
    /// <summary>No controller output.</summary>
    None,

    /// <summary>Xbox 360 compatible output.</summary>
    Xbox360,

    /// <summary>DualShock 4 compatible output.</summary>
    Ds4,
}

/// <summary>Feature groups an endpoint can read or write.</summary>
[Flags]
public enum ControllerFeatures
{
    /// <summary>No features.</summary>
    None = 0,

    /// <summary>Buttons, sticks, and triggers.</summary>
    StandardControls = 1 << 0,

    /// <summary>Gyro and accelerometer values.</summary>
    Motion = 1 << 1,

    /// <summary>Touchpad state.</summary>
    Touchpad = 1 << 2,

    /// <summary>Rumble feedback.</summary>
    Rumble = 1 << 3,

    /// <summary>Player light or LED state.</summary>
    Light = 1 << 4,

    /// <summary>Adaptive trigger feedback.</summary>
    AdaptiveTriggers = 1 << 5,
}

/// <summary>Standard gamepad button flags.</summary>
[Flags]
public enum ControllerButtons
{
    /// <summary>No buttons.</summary>
    None = 0,

    /// <summary>Bottom face button.</summary>
    South = 1 << 0,

    /// <summary>Right face button.</summary>
    East = 1 << 1,

    /// <summary>Left face button.</summary>
    West = 1 << 2,

    /// <summary>Top face button.</summary>
    North = 1 << 3,

    /// <summary>View, select, or back button.</summary>
    Back = 1 << 4,

    /// <summary>System guide button.</summary>
    Guide = 1 << 5,

    /// <summary>Menu, options, or start button.</summary>
    Start = 1 << 6,

    /// <summary>Left stick click.</summary>
    LeftStick = 1 << 7,

    /// <summary>Right stick click.</summary>
    RightStick = 1 << 8,

    /// <summary>Left shoulder button.</summary>
    LeftShoulder = 1 << 9,

    /// <summary>Right shoulder button.</summary>
    RightShoulder = 1 << 10,

    /// <summary>D-pad up.</summary>
    DPadUp = 1 << 11,

    /// <summary>D-pad down.</summary>
    DPadDown = 1 << 12,

    /// <summary>D-pad left.</summary>
    DPadLeft = 1 << 13,

    /// <summary>D-pad right.</summary>
    DPadRight = 1 << 14,
}

/// <summary>Buttons, sticks, and triggers.</summary>
public readonly record struct ControllerStandardState(
    ControllerButtons Buttons,
    short LeftX,
    short LeftY,
    short RightX,
    short RightY,
    ushort LeftTrigger,
    ushort RightTrigger);

/// <summary>Motion sensor state.</summary>
public readonly record struct ControllerMotionState(
    bool HasGyro,
    float GyroX,
    float GyroY,
    float GyroZ,
    bool HasAccelerometer,
    float AccelX,
    float AccelY,
    float AccelZ);

/// <summary>Touchpad state placeholder for future PlayStation-style features.</summary>
public readonly record struct ControllerTouchpadState(bool IsTouched, float X, float Y);

/// <summary>One controller input state, with optional feature groups.</summary>
public readonly record struct ControllerState(
    ControllerStandardState? Standard,
    ControllerMotionState? Motion,
    ControllerTouchpadState? Touchpad)
{
    /// <summary>Empty state.</summary>
    public static ControllerState Empty => default;
}

/// <summary>Rumble feedback.</summary>
public readonly record struct ControllerRumble(ushort LowFrequency, ushort HighFrequency);

/// <summary>LED or player-light feedback.</summary>
public readonly record struct ControllerLight(
    byte Red,
    byte Green,
    byte Blue,
    byte FlashOn = 0,
    byte FlashOff = 0);

/// <summary>Adaptive trigger feedback placeholder for future PlayStation-style features.</summary>
public readonly record struct ControllerAdaptiveTriggers(byte LeftMode, byte RightMode);

/// <summary>Output-to-controller feedback, with optional feature groups.</summary>
public readonly record struct ControllerFeedback(
    ControllerRumble? Rumble = null,
    ControllerLight? Light = null,
    ControllerAdaptiveTriggers? AdaptiveTriggers = null)
{
    /// <summary>Feature groups required to apply this feedback.</summary>
    public ControllerFeatures RequiredFeatures =>
        (Rumble is null ? ControllerFeatures.None : ControllerFeatures.Rumble) |
        (Light is null ? ControllerFeatures.None : ControllerFeatures.Light) |
        (AdaptiveTriggers is null ? ControllerFeatures.None : ControllerFeatures.AdaptiveTriggers);

    /// <summary>Gets whether this feedback carries any feature state.</summary>
    public bool IsEmpty => RequiredFeatures == ControllerFeatures.None;

    /// <summary>Gets the feedback subset supported by one endpoint.</summary>
    public ControllerFeedback ForFeatures(ControllerFeatures features)
    {
        return new ControllerFeedback(
            (features & ControllerFeatures.Rumble) == 0 ? null : Rumble,
            (features & ControllerFeatures.Light) == 0 ? null : Light,
            (features & ControllerFeatures.AdaptiveTriggers) == 0 ? null : AdaptiveTriggers);
    }

    /// <summary>Feedback that stops held rumble.</summary>
    public static ControllerFeedback StopRumble { get; } =
        new(new ControllerRumble(0, 0));
}
