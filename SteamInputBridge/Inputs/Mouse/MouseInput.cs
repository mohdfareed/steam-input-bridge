using System;

namespace SteamInputBridge.Inputs.Mouse;

/// <summary>Mouse button flags.</summary>
[Flags]
public enum MouseButtons
{
    /// <summary>No buttons.</summary>
    None = 0,

    /// <summary>Left button.</summary>
    Left = 1 << 0,

    /// <summary>Right button.</summary>
    Right = 1 << 1,

    /// <summary>Middle button.</summary>
    Middle = 1 << 2,

    /// <summary>Back button.</summary>
    Back = 1 << 3,

    /// <summary>Forward button.</summary>
    Forward = 1 << 4,
}

/// <summary>Relative mouse movement and button state.</summary>
public readonly record struct MouseReport(
    MouseButtons Buttons,
    int DeltaX,
    int DeltaY,
    int WheelDelta)
{
    /// <summary>Empty mouse report.</summary>
    public static MouseReport Empty => default;
}

/// <summary>Mouse report with source metadata.</summary>
public readonly record struct MouseInput(
    MouseReport Report,
    string? DeviceName, // Used for VIIPER mouse loopback filtering.
    nint DeviceHandle = default);

/// <summary>Handles one mouse input report.</summary>
public delegate void MouseInputHandler(in MouseInput input);
