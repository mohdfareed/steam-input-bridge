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
    public void IsMouseDeviceNameIdentifiesOwnedViiperMouseCaseInsensitively()
    {
        Assert.IsTrue(ViiperDevices.IsMouseDeviceName(@"\\?\HID#VID_6969&PID_5050#test"));
        Assert.IsTrue(ViiperDevices.IsMouseDeviceName(@"\\?\hid#vid_6969&pid_5050#test"));
        Assert.IsFalse(ViiperDevices.IsMouseDeviceName(@"\\?\HID#VID_6969&PID_0001#test"));
        Assert.IsFalse(ViiperDevices.IsMouseDeviceName(null));
    }
}
