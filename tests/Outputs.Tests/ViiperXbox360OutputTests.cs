using System;
using System.Threading.Tasks;
using Outputs.Viiper;
using ViiperDeviceInfo = global::Viiper.Client.Types.Device;
using ViiperXbox360Input = global::Viiper.Client.Devices.Xbox360.Xbox360Input;
using ViiperXbox360OutputReport = global::Viiper.Client.Devices.Xbox360.Xbox360Output;

namespace Outputs.Tests;

/// <summary>Tests for <see cref="ViiperXbox360Output" />.</summary>
[TestClass]
public sealed class ViiperXbox360OutputTests
{
    /// <summary>Checks direct field mapping.</summary>
    [TestMethod]
    public void MapReportPreservesSupportedFields()
    {
        Xbox360Report report = new(
            Xbox360Buttons.A | Xbox360Buttons.B | Xbox360Buttons.LeftShoulder,
            12,
            34,
            123,
            -456,
            789,
            -987);

        ViiperXbox360Input input = ViiperXbox360Output.MapReport(report);

        Assert.AreEqual((uint)report.Buttons, input.Buttons);
        Assert.AreEqual((byte)12, input.Lt);
        Assert.AreEqual((byte)34, input.Rt);
        Assert.AreEqual((short)123, input.Lx);
        Assert.AreEqual((short)-456, input.Ly);
        Assert.AreEqual((short)789, input.Rx);
        Assert.AreEqual((short)-987, input.Ry);
    }

    /// <summary>Checks rumble feedback mapping.</summary>
    [TestMethod]
    public void MapRumblePreservesMotors()
    {
        ViiperXbox360OutputReport output = new()
        {
            Left = 12,
            Right = 34,
        };

        Xbox360Rumble rumble = ViiperXbox360Output.MapRumble(output);

        Assert.AreEqual((byte)12, rumble.LeftMotor);
        Assert.AreEqual((byte)34, rumble.RightMotor);
    }

    /// <summary>Checks constructor argument validation.</summary>
    [TestMethod]
    public void ConstructorThrowsWhenDeviceIsNull()
    {
        try
        {
#pragma warning disable CA2000
            _ = new ViiperXbox360Output(null!);
#pragma warning restore CA2000
            Assert.Fail("Expected ArgumentNullException.");
        }
        catch (ArgumentNullException)
        {
        }
    }

    /// <summary>Checks owned device detection.</summary>
    [TestMethod]
    public void IsOwnedDeviceReturnsTrueForOwnedXbox360()
    {
        ViiperDeviceInfo device = new()
        {
            BusID = 1,
            DeviceSpecific = [],
            DevId = "1",
            Pid = ViiperXbox360Output.FormatUsbId(ViiperXbox360Output.OwnedProductId),
            Type = "xbox360",
            Vid = ViiperXbox360Output.FormatUsbId(ViiperXbox360Output.OwnedVendorId),
        };

        Assert.IsTrue(ViiperXbox360Output.IsOwnedDevice(device));
    }

    /// <summary>Checks foreign device detection.</summary>
    [TestMethod]
    public void IsOwnedDeviceReturnsFalseForForeignXbox360()
    {
        ViiperDeviceInfo device = new()
        {
            BusID = 1,
            DeviceSpecific = [],
            DevId = "1",
            Pid = "0x0001",
            Type = "xbox360",
            Vid = ViiperXbox360Output.FormatUsbId(ViiperXbox360Output.OwnedVendorId),
        };

        Assert.IsFalse(ViiperXbox360Output.IsOwnedDevice(device));
    }

    /// <summary>Checks source identity matching for the VIIPER-owned output device.</summary>
    [TestMethod]
    public void IsOwnedDeviceNameReturnsTrueForOwnedDeviceName()
    {
        const string Owned = @"\\?\HID#VIIPER&VID_045E&PID_028E#1";
        const string Foreign = @"\\?\HID#VID_0001&PID_028E#1";
        const string PhysicalXbox360 = @"\\?\HID#VID_045E&PID_028E#1";

        Assert.IsTrue(ViiperXbox360Output.IsOwnedDeviceName(Owned));
        Assert.IsFalse(ViiperXbox360Output.IsOwnedDeviceName(Foreign));
        Assert.IsFalse(ViiperXbox360Output.IsOwnedDeviceName(PhysicalXbox360));
        Assert.IsTrue(ViiperXbox360Output.IsOwnedSdlDevice("Controller", Owned));
    }

    /// <summary>Checks single-owner behavior.</summary>
    [TestMethod]
    public void TryAcquireOwnershipReturnsNullWhenAlreadyOwned()
    {
        string ownershipName = $@"Local\Outputs.Viiper.Xbox360.Tests.{Guid.NewGuid():N}";
        ViiperOutputOwnership? first = ViiperXbox360Output.TryAcquireOwnership(ownershipName);
        Assert.IsNotNull(first);

        Task<bool> secondAcquireTask = Task.Run(() =>
        {
            using ViiperOutputOwnership? second = ViiperXbox360Output.TryAcquireOwnership(ownershipName);
            return second is not null;
        });

        bool secondAcquired;
        try
        {
            secondAcquired = secondAcquireTask.GetAwaiter().GetResult();
        }
        finally
        {
            first.Dispose();
        }

        Assert.IsFalse(secondAcquired);
    }

    /// <summary>Checks same-thread ownership rejection.</summary>
    [TestMethod]
    public void TryAcquireOwnershipReturnsNullWhenAlreadyOwnedOnSameThread()
    {
        string ownershipName = $@"Local\Outputs.Viiper.Xbox360.Tests.{Guid.NewGuid():N}";
        using ViiperOutputOwnership? first = ViiperXbox360Output.TryAcquireOwnership(ownershipName);
        using ViiperOutputOwnership? second = ViiperXbox360Output.TryAcquireOwnership(ownershipName);

        Assert.IsNotNull(first);
        Assert.IsNull(second);
    }
}
