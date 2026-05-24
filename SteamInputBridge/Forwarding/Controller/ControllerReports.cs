using System;

namespace SteamInputBridge.Forwarding.Controller;

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
    short RightY)
{
    /// <summary>Centered controller state.</summary>
    public static Xbox360Report Empty => default;

    /// <summary>Gets whether the report carries no input.</summary>
    public bool IsEmpty =>
        Buttons == Xbox360Buttons.None &&
        LeftTrigger == 0 &&
        RightTrigger == 0 &&
        LeftX == 0 &&
        LeftY == 0 &&
        RightX == 0 &&
        RightY == 0;
}

/// <summary>Xbox 360 rumble feedback.</summary>
public readonly record struct Xbox360Rumble(byte LeftMotor, byte RightMotor);

/// <summary>DualShock 4 button flags.</summary>
[Flags]
public enum Ds4Buttons
{
    /// <summary>No buttons.</summary>
    None = 0,

    /// <summary>PlayStation button.</summary>
    PlayStation = 0x0001,

    /// <summary>Touchpad click.</summary>
    TouchpadClick = 0x0002,

    /// <summary>Square button.</summary>
    Square = 0x0010,

    /// <summary>Cross button.</summary>
    Cross = 0x0020,

    /// <summary>Circle button.</summary>
    Circle = 0x0040,

    /// <summary>Triangle button.</summary>
    Triangle = 0x0080,

    /// <summary>L1 shoulder button.</summary>
    L1 = 0x0100,

    /// <summary>R1 shoulder button.</summary>
    R1 = 0x0200,

    /// <summary>L2 trigger button.</summary>
    L2 = 0x0400,

    /// <summary>R2 trigger button.</summary>
    R2 = 0x0800,

    /// <summary>Share button.</summary>
    Share = 0x1000,

    /// <summary>Options button.</summary>
    Options = 0x2000,

    /// <summary>L3 stick button.</summary>
    L3 = 0x4000,

    /// <summary>R3 stick button.</summary>
    R3 = 0x8000,
}

/// <summary>DualShock 4 VIIPER d-pad bitfield.</summary>
[Flags]
public enum Ds4DPad
{
    /// <summary>D-pad released.</summary>
    None = 0,

    /// <summary>D-pad up.</summary>
    Up = 0x01,

    /// <summary>D-pad down.</summary>
    Down = 0x02,

    /// <summary>D-pad left.</summary>
    Left = 0x04,

    /// <summary>D-pad right.</summary>
    Right = 0x08,

    /// <summary>D-pad up/right.</summary>
    UpRight = Up | Right,

    /// <summary>D-pad down/right.</summary>
    DownRight = Down | Right,

    /// <summary>D-pad down/left.</summary>
    DownLeft = Down | Left,

    /// <summary>D-pad up/left.</summary>
    UpLeft = Up | Left,
}

/// <summary>DualShock 4 controller state report.</summary>
public readonly record struct Ds4Report(
    Ds4Buttons Buttons,
    Ds4DPad DPad,
    sbyte LeftX,
    sbyte LeftY,
    sbyte RightX,
    sbyte RightY,
    byte LeftTrigger,
    byte RightTrigger,
    ushort Touch1X,
    ushort Touch1Y,
    bool Touch1Active,
    ushort Touch2X,
    ushort Touch2Y,
    bool Touch2Active,
    short GyroX,
    short GyroY,
    short GyroZ,
    short AccelX,
    short AccelY,
    short AccelZ)
{
    /// <summary>Default accelerometer value for a flat DS4.</summary>
    public const short DefaultAccelZ = -5023;

    /// <summary>Maximum DS4 touchpad X coordinate.</summary>
    public const ushort TouchpadMaxX = 1920;

    /// <summary>Maximum DS4 touchpad Y coordinate.</summary>
    public const ushort TouchpadMaxY = 942;

    /// <summary>Centered controller state.</summary>
    public static Ds4Report Empty => new(
        Ds4Buttons.None,
        Ds4DPad.None,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        false,
        0,
        0,
        false,
        0,
        0,
        0,
        0,
        0,
        DefaultAccelZ);

    /// <summary>Gets whether the report carries no active input.</summary>
    public bool IsEmpty =>
        Buttons == Ds4Buttons.None &&
        DPad == Ds4DPad.None &&
        LeftX == 0 &&
        LeftY == 0 &&
        RightX == 0 &&
        RightY == 0 &&
        LeftTrigger == 0 &&
        RightTrigger == 0 &&
        !Touch1Active &&
        !Touch2Active &&
        GyroX == 0 &&
        GyroY == 0 &&
        GyroZ == 0 &&
        AccelX == 0 &&
        AccelY == 0 &&
        AccelZ == DefaultAccelZ;
}

/// <summary>DualShock 4 output feedback.</summary>
public readonly record struct Ds4Feedback(
    byte SmallRumble,
    byte LargeRumble,
    byte LedRed,
    byte LedGreen,
    byte LedBlue,
    byte FlashOn,
    byte FlashOff);
