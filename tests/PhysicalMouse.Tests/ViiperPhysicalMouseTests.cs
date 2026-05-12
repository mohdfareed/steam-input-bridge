using System;
using PhysicalMouse.Viiper;
using ViiperMouseInput = global::Viiper.Client.Devices.Mouse.MouseInput;

namespace PhysicalMouse.Tests;

/// <summary>
/// Tests for <see cref="ViiperPhysicalMouse" />.
/// </summary>
[TestClass]
public sealed class ViiperPhysicalMouseTests
{
    /// <summary>
    /// Checks direct field mapping.
    /// </summary>
    [TestMethod]
    public void MapReportPreservesSupportedFields()
    {
        MouseReport report = new(
            MouseButtons.Left | MouseButtons.Right | MouseButtons.Forward,
            123,
            -456,
            7);

        ViiperMouseInput input = ViiperPhysicalMouse.MapReport(report);

        Assert.AreEqual((byte)(MouseButtons.Left | MouseButtons.Right | MouseButtons.Forward), input.Buttons);
        Assert.AreEqual((short)123, input.Dx);
        Assert.AreEqual((short)-456, input.Dy);
        Assert.AreEqual((short)7, input.Wheel);
        Assert.AreEqual((short)0, input.Pan);
    }

    /// <summary>
    /// Checks overflow behavior.
    /// </summary>
    [TestMethod]
    public void MapReportThrowsWhenDeltaXOverflowsViiperRange()
    {
        MouseReport report = new(MouseButtons.None, short.MaxValue + 1, 0, 0);

        try
        {
            _ = ViiperPhysicalMouse.MapReport(report);
            Assert.Fail("Expected OverflowException.");
        }
        catch (OverflowException)
        {
        }
    }

    /// <summary>
    /// Checks constructor argument validation.
    /// </summary>
    [TestMethod]
    public void ConstructorThrowsWhenDeviceIsNull()
    {
        try
        {
#pragma warning disable CA2000
            _ = new ViiperPhysicalMouse(null!);
#pragma warning restore CA2000
            Assert.Fail("Expected ArgumentNullException.");
        }
        catch (ArgumentNullException)
        {
        }
    }
}
