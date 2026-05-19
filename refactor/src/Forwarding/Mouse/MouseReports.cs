using System;
using System.Threading;
using System.Threading.Tasks;

namespace VirtualMouse.Forwarding;

// MARK: Reports
// ============================================================================

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

    /// <summary>Gets whether the report carries no input.</summary>
    public bool IsEmpty =>
        Buttons == MouseButtons.None &&
        DeltaX == 0 &&
        DeltaY == 0 &&
        WheelDelta == 0;
}

/// <summary>Mouse report with source metadata.</summary>
public readonly record struct MouseInput(
    MouseReport Report,
    string? DeviceName,
    nint DeviceHandle = default);

// MARK: Endpoints
// ============================================================================

/// <summary>Connected mouse input source.</summary>
public interface IMouseInputSource : IAsyncDisposable
{
    /// <summary>Gets whether the source is connected.</summary>
    bool IsConnected { get; }

    /// <summary>Runs the source until cancelled.</summary>
    void Run(MouseInputHandler handler, CancellationToken cancellationToken = default);
}

/// <summary>Handles one mouse input report.</summary>
public delegate void MouseInputHandler(in MouseInput input);

/// <summary>Connected mouse output.</summary>
public interface IMouseOutput : IAsyncDisposable
{
    /// <summary>Gets whether the output is connected.</summary>
    bool IsConnected { get; }

    /// <summary>Returns whether an input report came from this output and must be ignored.</summary>
    bool FilterInput(in MouseInput input)
    {
        _ = input;
        return false;
    }

    /// <summary>Sends one mouse report.</summary>
    ValueTask SendAsync(MouseReport report, CancellationToken cancellationToken = default);
}
