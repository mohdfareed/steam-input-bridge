using System;
using System.Threading;
using System.Threading.Tasks;
using VirtualMouse.Forwarding;
using VirtualMouse.Outputs.Teensy;

namespace VirtualMouse.Tests;

/// <summary>Tests Teensy placeholder behavior.</summary>
[TestClass]
public sealed class TeensyOutputTests
{
    /// <summary>Teensy mouse output is wired but not implemented.</summary>
    [TestMethod]
    public async Task TeensyMouseOutputThrowsUntilImplemented()
    {
        await using IMouseOutput output = new TeensyOutputFactory().Connect(MouseOutput.Teensy);

        Assert.IsFalse(output.IsConnected);
        _ = Assert.ThrowsExactly<NotImplementedException>(() =>
            output.SendAsync(MouseReport.Empty, CancellationToken.None)
                .AsTask()
                .GetAwaiter()
                .GetResult());
    }
}
