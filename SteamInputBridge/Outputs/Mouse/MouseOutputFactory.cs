using System;
using System.Threading;
using System.Threading.Tasks;
using SteamInputBridge.Outputs.Teensy;
using SteamInputBridge.Outputs.Viiper.Mouse;

namespace SteamInputBridge.Outputs.Mouse;

/// <summary>Creates configured mouse outputs.</summary>
public sealed class MouseOutputFactory(
    ViiperMouseOutputFactory viiper,
    TeensyMouseOutputFactory teensy) : IMouseOutputFactory
{
    /// <inheritdoc />
    public ValueTask<IMouseOutput> ConnectAsync(MouseOutput output, CancellationToken cancellationToken = default)
    {
        return output switch
        {
            MouseOutput.Viiper => viiper.ConnectAsync(output, cancellationToken),
            MouseOutput.Teensy => teensy.ConnectAsync(output, cancellationToken),
            MouseOutput.None => throw new NotSupportedException("None is not a mouse output."),
            _ => throw new NotSupportedException($"Unsupported mouse output {output}."),
        };
    }
}
