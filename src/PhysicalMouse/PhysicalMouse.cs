using System;
using System.Threading;
using System.Threading.Tasks;

namespace PhysicalMouse;

/// <summary>Sends mouse reports to a transport.</summary>
public interface IPhysicalMouse : IAsyncDisposable
{
    /// <summary>Gets whether the transport is connected.</summary>
    bool IsConnected { get; }

    /// <summary>Sends one mouse report.</summary>
    /// <param name="report">Report to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask SendAsync(MouseReport report, CancellationToken cancellationToken = default);
}

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

/// <summary>Relative mouse input.</summary>
/// <param name="Buttons">Current button state.</param>
/// <param name="DeltaX">Horizontal delta.</param>
/// <param name="DeltaY">Vertical delta.</param>
/// <param name="WheelDelta">Wheel delta.</param>
public readonly record struct MouseReport(
    MouseButtons Buttons,
    int DeltaX,
    int DeltaY,
    int WheelDelta)
{
    /// <summary>Empty input.</summary>
    public static MouseReport Empty => default;

    /// <summary>Gets whether the report has no input.</summary>
    public bool IsEmpty =>
        Buttons == MouseButtons.None &&
        DeltaX == 0 &&
        DeltaY == 0 &&
        WheelDelta == 0;
}
