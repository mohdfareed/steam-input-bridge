using System;
using VirtualMouse.Forwarding;
using VirtualMouse.Outputs.Teensy;
using VirtualMouse.Outputs.Viiper;

namespace VirtualMouse.Hosting;

internal sealed class ServerMouseOutputFactory(
    ViiperOutputFactory viiper,
    TeensyOutputFactory teensy) : IMouseOutputFactory
{
    public IMouseOutput Connect(MouseOutput output)
    {
        return output switch
        {
            MouseOutput.Viiper => viiper.Connect(output),
            MouseOutput.Teensy => teensy.Connect(output),
            MouseOutput.None => throw new NotSupportedException("None is not a mouse output."),
            _ => throw new NotSupportedException($"Unsupported mouse output: {output}."),
        };
    }
}
