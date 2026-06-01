using System;
using System.Threading;
using System.Threading.Tasks;
using SteamInputBridge.Outputs.Mouse;

namespace SteamInputBridge.Outputs.Teensy;

/// <summary>Creates Teensy mouse outputs.</summary>
public sealed class TeensyMouseOutputFactory : IMouseOutputFactory
{
    /// <summary>Connects a Teensy mouse output.</summary>
    public ValueTask<IMouseOutput> ConnectAsync(MouseOutput output, CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        return output == MouseOutput.Teensy
            ? throw new NotSupportedException("Teensy mouse output is not implemented yet.")
            : throw new NotSupportedException($"Teensy does not support {output} mouse output.");
    }
}
