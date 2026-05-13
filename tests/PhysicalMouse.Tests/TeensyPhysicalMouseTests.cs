using System;
using System.Threading.Tasks;
using PhysicalMouse.Teensy;

namespace PhysicalMouse.Tests;

/// <summary>Tests for <see cref="TeensyPhysicalMouse" />.</summary>
[TestClass]
public sealed class TeensyPhysicalMouseTests
{
    /// <summary>Checks disconnected state.</summary>
    [TestMethod]
    public async Task IsConnectedIsFalse()
    {
        TeensyPhysicalMouse transport = new();

        try
        {
            Assert.IsFalse(transport.IsConnected);
        }
        finally
        {
            await transport.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Checks the current placeholder behavior.</summary>
    [TestMethod]
    public async Task SendAsyncThrowsNotImplementedException()
    {
        TeensyPhysicalMouse transport = new();

        try
        {
            try
            {
                await transport.SendAsync(MouseReport.Empty).ConfigureAwait(false);
                Assert.Fail("Expected NotImplementedException.");
            }
            catch (NotImplementedException)
            {
            }
        }
        finally
        {
            await transport.DisposeAsync().ConfigureAwait(false);
        }
    }
}
