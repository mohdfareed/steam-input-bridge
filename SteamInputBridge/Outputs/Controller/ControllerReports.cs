using System;

namespace SteamInputBridge.Outputs.Controller;

/// <summary>Xbox 360 button flags.</summary>
[Flags]
public enum Xbox360Buttons
{
    /// <summary>No buttons.</summary>
    None = 0,

    /// <summary>D-pad up.</summary>
    DPadUp = 1 << 0,

    /// <summary>D-pad down.</summary>
    DPadDown = 1 << 1,

    /// <summary>D-pad left.</summary>
    DPadLeft = 1 << 2,

    /// <summary>D-pad right.</summary>
    DPadRight = 1 << 3,

    /// <summary>Start button.</summary>
    Start = 1 << 4,

    /// <summary>Back button.</summary>
    Back = 1 << 5,

    /// <summary>Left thumbstick button.</summary>
    LeftThumb = 1 << 6,

    /// <summary>Right thumbstick button.</summary>
    RightThumb = 1 << 7,

    /// <summary>Left shoulder button.</summary>
    LeftShoulder = 1 << 8,

    /// <summary>Right shoulder button.</summary>
    RightShoulder = 1 << 9,

    /// <summary>Guide button.</summary>
    Guide = 1 << 10,

    /// <summary>A button.</summary>
    A = 1 << 12,

    /// <summary>B button.</summary>
    B = 1 << 13,

    /// <summary>X button.</summary>
    X = 1 << 14,

    /// <summary>Y button.</summary>
    Y = 1 << 15,
}

/// <summary>Xbox 360 controller state report.</summary>
public readonly record struct Xbox360Report(
    Xbox360Buttons Buttons,
    byte LeftTrigger,
    byte RightTrigger,
    short LeftX,
    short LeftY,
    short RightX,
    short RightY);

/// <summary>Xbox 360 rumble feedback.</summary>
public readonly record struct Xbox360Rumble(byte LeftMotor, byte RightMotor);
