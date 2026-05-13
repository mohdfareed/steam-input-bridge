using System;
using System.Threading;
using System.Threading.Tasks;

namespace PhysicalMouse.Teensy;

/// <summary>Teensy 4.0 transport.</summary>
public sealed class TeensyPhysicalMouse : IPhysicalMouse
{
    /// <inheritdoc />
    public bool IsConnected => false;

    /// <inheritdoc />
    public ValueTask SendAsync(MouseReport report, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Teensy 4.0 transport is not implemented yet.");
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
