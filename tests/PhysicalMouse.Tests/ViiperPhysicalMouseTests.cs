using System;
using System.Threading.Tasks;
using PhysicalMouse.Viiper;
using ViiperDeviceInfo = global::Viiper.Client.Types.Device;
using ViiperMouseInput = global::Viiper.Client.Devices.Mouse.MouseInput;

namespace PhysicalMouse.Tests;

/// <summary>Tests for <see cref="ViiperPhysicalMouse" />.</summary>
[TestClass]
public sealed class ViiperPhysicalMouseTests
{
    /// <summary>Checks direct field mapping.</summary>
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

    /// <summary>Checks overflow behavior.</summary>
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

    /// <summary>Checks constructor argument validation.</summary>
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

    /// <summary>Checks sticky option validation.</summary>
    [TestMethod]
    public async Task ConnectAsyncThrowsWhenDeviceIdHasNoBusId()
    {
        ViiperOptions options = new()
        {
            DeviceId = "1",
        };

        try
        {
            _ = await ViiperPhysicalMouse.ConnectAsync(options).ConfigureAwait(false);
            Assert.Fail("Expected ArgumentException.");
        }
        catch (ArgumentException)
        {
        }
    }

    /// <summary>Checks reusable device selection.</summary>
    [TestMethod]
    public void SelectReusableDeviceReturnsSingleMouse()
    {
        ViiperDeviceInfo[] devices =
        [
            new ViiperDeviceInfo
            {
                BusID = 7,
                DeviceSpecific = [],
                DevId = "1",
                Pid = string.Empty,
                Type = "keyboard",
                Vid = string.Empty,
            },
            new ViiperDeviceInfo
            {
                BusID = 7,
                DeviceSpecific = [],
                DevId = "2",
                Pid = string.Empty,
                Type = "mouse",
                Vid = string.Empty,
            },
        ];

        ViiperDeviceInfo? reusable = ViiperPhysicalMouse.SelectReusableDevice(devices);

        Assert.IsNotNull(reusable);
        Assert.AreEqual("2", reusable.DevId);
    }

    /// <summary>Checks ambiguous device selection.</summary>
    [TestMethod]
    public void SelectReusableDeviceReturnsNullForMultipleMice()
    {
        ViiperDeviceInfo[] devices =
        [
            new ViiperDeviceInfo
            {
                BusID = 7,
                DeviceSpecific = [],
                DevId = "1",
                Pid = string.Empty,
                Type = "mouse",
                Vid = string.Empty,
            },
            new ViiperDeviceInfo
            {
                BusID = 7,
                DeviceSpecific = [],
                DevId = "2",
                Pid = string.Empty,
                Type = "mouse",
                Vid = string.Empty,
            },
        ];

        ViiperDeviceInfo? reusable = ViiperPhysicalMouse.SelectReusableDevice(devices);

        Assert.IsNull(reusable);
    }
}
