using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace VirtualMouse.Forwarding;

// MARK: Models
// ============================================================================

/// <summary>Controller input sent from one client-owned controller stream.</summary>
public readonly record struct ControllerInputFrame(ushort ControllerIndex, ControllerState State);

/// <summary>Controller feedback sent back to one client-owned controller stream.</summary>
public readonly record struct ControllerFeedbackFrame(ushort ControllerIndex, ControllerFeedback Feedback);

/// <summary>A controller hot-path pipe message.</summary>
public readonly record struct ControllerPipeMessage(
    ControllerPipeFrameType Type,
    ControllerInputFrame Input,
    ControllerFeedbackFrame Feedback);

/// <summary>Hot-path controller pipe frame type.</summary>
public enum ControllerPipeFrameType
{
    /// <summary>No frame type.</summary>
    None = 0,

    /// <summary>Controller input frame.</summary>
    Input = 1,

    /// <summary>Controller feedback frame.</summary>
    Feedback = 2,
}

// MARK: Stream API
// ============================================================================

/// <summary>Writes fixed-size controller hot-path frames.</summary>
public sealed class ControllerPipeWriter(Stream stream)
{
    private readonly byte[] _buffer = new byte[ControllerPipeFrame.Size];

    /// <summary>Writes one controller input frame.</summary>
    public ValueTask WriteInputAsync(ControllerInputFrame frame, CancellationToken cancellationToken = default)
    {
        ControllerPipeFrame.WriteInput(_buffer, frame);
        return stream.WriteAsync(_buffer, cancellationToken);
    }

    /// <summary>Writes one controller feedback frame.</summary>
    public ValueTask WriteFeedbackAsync(ControllerFeedbackFrame frame, CancellationToken cancellationToken = default)
    {
        ControllerPipeFrame.WriteFeedback(_buffer, frame);
        return stream.WriteAsync(_buffer, cancellationToken);
    }
}

/// <summary>Reads fixed-size controller hot-path frames.</summary>
public sealed class ControllerPipeReader(Stream stream)
{
    private readonly byte[] _buffer = new byte[ControllerPipeFrame.Size];

    /// <summary>Reads the next controller input or feedback frame.</summary>
    public async ValueTask<ControllerPipeMessage> ReadAsync(CancellationToken cancellationToken = default)
    {
        await stream.ReadExactlyAsync(_buffer, cancellationToken).ConfigureAwait(false);
        return ControllerPipeFrame.Read(_buffer);
    }
}
