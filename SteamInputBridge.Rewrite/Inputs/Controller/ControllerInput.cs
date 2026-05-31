using System;

namespace SteamInputBridge.Inputs.Controller;

/// <summary>Controller button flags used by the Steam Controller path.</summary>
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

    /// <summary>Back/select button.</summary>
    Back = 1 << 4,

    /// <summary>Guide button.</summary>
    Guide = 1 << 5,

    /// <summary>Start/menu button.</summary>
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

/// <summary>One controller input state.</summary>
public readonly record struct ControllerState(
    ControllerButtons Buttons,
    short LeftX,
    short LeftY,
    short RightX,
    short RightY,
    ushort LeftTrigger,
    ushort RightTrigger)
{
    /// <summary>Centered controller state.</summary>
    public static ControllerState Empty => default;
}

/// <summary>Controller rumble feedback.</summary>
public readonly record struct ControllerRumble(ushort LowFrequency, ushort HighFrequency)
{
    /// <summary>Stops both rumble motors.</summary>
    public static ControllerRumble Stop => default;
}
