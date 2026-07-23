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

internal static class MouseReportSegmentation
{
    public static bool FitsInInt16(in MouseReport report)
    {
        return report.DeltaX is >= short.MinValue and <= short.MaxValue &&
            report.DeltaY is >= short.MinValue and <= short.MaxValue &&
            report.WheelDelta is >= short.MinValue and <= short.MaxValue;
    }

    public static bool HasDeltas(in MouseReport report)
    {
        return report.DeltaX != 0 || report.DeltaY != 0 || report.WheelDelta != 0;
    }

    public static MouseReport TakeSegment(ref MouseReport remaining)
    {
        int deltaX = Math.Clamp(remaining.DeltaX, short.MinValue, short.MaxValue);
        int deltaY = Math.Clamp(remaining.DeltaY, short.MinValue, short.MaxValue);
        int wheelDelta = Math.Clamp(remaining.WheelDelta, short.MinValue, short.MaxValue);

        MouseReport segment = new(remaining.Buttons, deltaX, deltaY, wheelDelta);
        remaining = new(
            remaining.Buttons,
            remaining.DeltaX - deltaX,
            remaining.DeltaY - deltaY,
            remaining.WheelDelta - wheelDelta);
        return segment;
    }
}

/// <summary>Mouse report with source metadata.</summary>
public readonly record struct MouseInput(
    MouseReport Report,
    string? DeviceName, // Used for VIIPER mouse loopback filtering.
    nint DeviceHandle = default);

/// <summary>Handles one mouse input report.</summary>
public delegate void MouseInputHandler(in MouseInput input);
