using System;
using System.Threading;
using System.Threading.Tasks;

namespace PhysicalMouse;

/// <summary>
/// Sends mouse reports to a physical mouse transport.
/// </summary>
public interface IPhysicalMouse
{
    /// <summary>
    /// Sends a single mouse report without applying any extra buffering or transformation.
    /// </summary>
    /// <param name="report">The report to send.</param>
    /// <param name="cancellationToken">Cancels the send operation.</param>
    ValueTask SendAsync(MouseReport report, CancellationToken cancellationToken = default);
}

/// <summary>
/// Mouse button state flags.
/// </summary>
[Flags]
public enum MouseButtons
{
    /// <summary>No buttons are pressed.</summary>
    None = 0,

    /// <summary>The left button is pressed.</summary>
    Left = 1 << 0,

    /// <summary>The right button is pressed.</summary>
    Right = 1 << 1,

    /// <summary>The middle button is pressed.</summary>
    Middle = 1 << 2,

    /// <summary>The back button is pressed.</summary>
    Back = 1 << 3,

    /// <summary>The forward button is pressed.</summary>
    Forward = 1 << 4,
}

/// <summary>
/// A single relative mouse input report.
/// </summary>
/// <param name="Buttons">The full current button state.</param>
/// <param name="DeltaX">The relative horizontal movement.</param>
/// <param name="DeltaY">The relative vertical movement.</param>
/// <param name="WheelDelta">The relative vertical wheel movement.</param>
public readonly record struct MouseReport(
    MouseButtons Buttons,
    int DeltaX,
    int DeltaY,
    int WheelDelta)
{
    /// <summary>
    /// An empty report with no movement, wheel input, or pressed buttons.
    /// </summary>
    public static MouseReport Empty => default;

    /// <summary>
    /// Gets a value indicating whether the report contains no movement, wheel input, or pressed buttons.
    /// </summary>
    public bool IsEmpty =>
        Buttons == MouseButtons.None &&
        DeltaX == 0 &&
        DeltaY == 0 &&
        WheelDelta == 0;
}
