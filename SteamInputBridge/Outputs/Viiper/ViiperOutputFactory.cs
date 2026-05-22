using System;
using System.Threading;
using System.Threading.Tasks;
using SteamInputBridge.Forwarding.Controller;
using SteamInputBridge.Forwarding.Mouse;

namespace SteamInputBridge.Outputs.Viiper;

/// <summary>Creates VIIPER outputs for forwarding routes.</summary>
internal sealed class ViiperOutputFactory(ViiperOptions options) : IControllerOutputFactory, IMouseOutputFactory
{
    /// <inheritdoc />
    public IControllerOutput Connect(ControllerId controllerId, ControllerOutput output)
    {
        return output switch
        {
            ControllerOutput.Xbox360 => ViiperXbox360Output.ConnectAsync(options, controllerId).GetAwaiter().GetResult(),
            ControllerOutput.None => throw new NotSupportedException("None is not a VIIPER controller output."),
            ControllerOutput.Ds4 => throw new NotSupportedException("VIIPER DS4 output is not implemented yet."),
            _ => throw new NotSupportedException($"VIIPER does not support {output} controller output yet."),
        };
    }

    /// <inheritdoc />
    public IMouseOutput Connect(MouseOutput output)
    {
        return output switch
        {
            MouseOutput.Viiper => ViiperMouseOutput.ConnectAsync(options).GetAwaiter().GetResult(),
            MouseOutput.None => throw new NotSupportedException("None is not a VIIPER mouse output."),
            MouseOutput.Teensy => throw new NotSupportedException("Teensy output is handled by the Teensy adapter."),
            _ => throw new NotSupportedException($"VIIPER does not support {output} mouse output."),
        };
    }

    /// <summary>Removes stale VIIPER devices created by this adapter.</summary>
    public Task ReclaimDevicesAsync(CancellationToken cancellationToken = default)
    {
        return ReclaimDevicesAsync(options, cancellationToken);
    }

    /// <summary>Removes stale VIIPER devices created by this adapter.</summary>
    public static async Task ReclaimDevicesAsync(ViiperOptions options, CancellationToken cancellationToken = default)
    {
        await ViiperXbox360Output.ReclaimDevicesAsync(options, cancellationToken)
            .ConfigureAwait(false);

        await ViiperMouseOutput.ReclaimDevicesAsync(options, cancellationToken)
            .ConfigureAwait(false);
    }
}
