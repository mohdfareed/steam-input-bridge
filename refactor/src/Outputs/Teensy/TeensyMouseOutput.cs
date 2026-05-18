using System;
using System.Threading;
using System.Threading.Tasks;
using VirtualMouse.Forwarding;

namespace VirtualMouse.Outputs.Teensy;

/// <summary>Creates Teensy outputs for forwarding routes.</summary>
public sealed class TeensyOutputFactory : IMouseOutputFactory
{
    /// <inheritdoc />
    public IMouseOutput Connect(MouseOutput output)
    {
        return output switch
        {
            MouseOutput.Teensy => new TeensyMouseOutput(),
            MouseOutput.None => throw new NotSupportedException("None is not a Teensy mouse output."),
            MouseOutput.Viiper => throw new NotSupportedException("VIIPER output is handled by the VIIPER adapter."),
            _ => throw new NotSupportedException($"Teensy does not support {output} mouse output."),
        };
    }
}

/// <summary>Teensy 4.0 mouse transport placeholder.</summary>
public sealed class TeensyMouseOutput : IMouseOutput
{
    /// <inheritdoc />
    public bool IsConnected => false;

    /// <inheritdoc />
    public ValueTask SendAsync(MouseReport report, CancellationToken cancellationToken = default)
    {
        _ = report;
        _ = cancellationToken;
        throw new NotImplementedException("Teensy 4.0 transport is not implemented yet.");
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
