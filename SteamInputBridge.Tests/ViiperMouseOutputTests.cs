using System.Collections.Generic;
using System.Linq;
using SteamInputBridge.Inputs.Mouse;
using SteamInputBridge.Outputs.Viiper.Mouse;

namespace SteamInputBridge.Tests;

[TestClass]
public sealed class ViiperMouseOutputTests
{
    [TestMethod]
    public void MapReportMapsButtonsMovementAndWheel()
    {
        MouseReport report = new(
            MouseButtons.Left | MouseButtons.Right | MouseButtons.Forward,
            DeltaX: 12,
            DeltaY: -34,
            WheelDelta: 120);

        global::Viiper.Client.Devices.Mouse.MouseInput input = ViiperMouseOutput.MapReport(report);

        Assert.AreEqual((byte)(MouseButtons.Left | MouseButtons.Right | MouseButtons.Forward), input.Buttons);
        Assert.AreEqual((short)12, input.Dx);
        Assert.AreEqual((short)-34, input.Dy);
        Assert.AreEqual((short)120, input.Wheel);
        Assert.AreEqual((short)0, input.Pan);
    }

    [TestMethod]
    public void SegmentedReportsPreserveOrderedSignedTotals()
    {
        MouseButtons buttons = MouseButtons.Left | MouseButtons.Back;
        MouseReport remaining = new(buttons, DeltaX: 70_000, DeltaY: -80_000, WheelDelta: 40_000);
        List<global::Viiper.Client.Devices.Mouse.MouseInput> inputs = [];

        while (MouseReportSegmentation.HasDeltas(in remaining))
        {
            MouseReport segment = MouseReportSegmentation.TakeSegment(ref remaining);
            inputs.Add(ViiperMouseOutput.MapReport(segment));
        }

        Assert.HasCount(3, inputs);
        CollectionAssert.AreEqual(
            new short[] { short.MaxValue, short.MaxValue, 4_466 },
            inputs.ConvertAll(x => x.Dx));
        CollectionAssert.AreEqual(
            new short[] { short.MinValue, short.MinValue, -14_464 },
            inputs.ConvertAll(x => x.Dy));
        CollectionAssert.AreEqual(new short[] { short.MaxValue, 7_233, 0 }, inputs.ConvertAll(x => x.Wheel));
        Assert.IsTrue(inputs.TrueForAll(input => input.Buttons == (byte)buttons));
        Assert.AreEqual(70_000, inputs.ConvertAll(x => (int)x.Dx).Sum());
        Assert.AreEqual(-80_000, inputs.ConvertAll(x => (int)x.Dy).Sum());
        Assert.AreEqual(40_000, inputs.ConvertAll(x => (int)x.Wheel).Sum());
    }

    [TestMethod]
    public void IsMouseDeviceNameIdentifiesOwnedViiperMouseCaseInsensitively()
    {
        Assert.IsTrue(ViiperDevices.IsMouseDeviceName(@"\\?\HID#VID_6969&PID_5050#test"));
        Assert.IsTrue(ViiperDevices.IsMouseDeviceName(@"\\?\hid#vid_6969&pid_5050#test"));
        Assert.IsFalse(ViiperDevices.IsMouseDeviceName(@"\\?\HID#VID_6969&PID_0001#test"));
        Assert.IsFalse(ViiperDevices.IsMouseDeviceName(null));
    }
}
