using SteamInputBridge.Inputs.Controller;
using SteamInputBridge.Outputs.Controller;
using SteamInputBridge.Outputs.Viiper.Controller;

namespace SteamInputBridge.Tests;

[TestClass]
public sealed class ControllerOutputMappingTests
{
    [TestMethod]
    public void ToXbox360ReportMapsButtonsAxesAndTriggers()
    {
        ControllerState state = new(
            ControllerButtons.South |
            ControllerButtons.East |
            ControllerButtons.West |
            ControllerButtons.North |
            ControllerButtons.Back |
            ControllerButtons.Guide |
            ControllerButtons.Start |
            ControllerButtons.LeftStick |
            ControllerButtons.RightStick |
            ControllerButtons.LeftShoulder |
            ControllerButtons.RightShoulder |
            ControllerButtons.DPadUp |
            ControllerButtons.DPadDown |
            ControllerButtons.DPadLeft |
            ControllerButtons.DPadRight,
            LeftX: 10,
            LeftY: -20,
            RightX: 30,
            RightY: short.MinValue,
            LeftTrigger: 32767,
            RightTrigger: 16384);

        Xbox360Report report = ControllerOutputMapping.ToXbox360Report(in state);

        Xbox360Buttons expected =
            Xbox360Buttons.A |
            Xbox360Buttons.B |
            Xbox360Buttons.X |
            Xbox360Buttons.Y |
            Xbox360Buttons.Back |
            Xbox360Buttons.Guide |
            Xbox360Buttons.Start |
            Xbox360Buttons.LeftThumb |
            Xbox360Buttons.RightThumb |
            Xbox360Buttons.LeftShoulder |
            Xbox360Buttons.RightShoulder |
            Xbox360Buttons.DPadUp |
            Xbox360Buttons.DPadDown |
            Xbox360Buttons.DPadLeft |
            Xbox360Buttons.DPadRight;
        Assert.AreEqual(expected, report.Buttons);
        Assert.AreEqual((byte)255, report.LeftTrigger);
        Assert.AreEqual((byte)127, report.RightTrigger);
        Assert.AreEqual((short)10, report.LeftX);
        Assert.AreEqual((short)20, report.LeftY);
        Assert.AreEqual((short)30, report.RightX);
        Assert.AreEqual(short.MaxValue, report.RightY);
    }

    [TestMethod]
    public void ToControllerRumbleMapsXboxMotorBytesToFullUShortRange()
    {
        ControllerRumble rumble = ControllerOutputMapping.ToControllerRumble(new Xbox360Rumble(byte.MaxValue, 128));

        Assert.AreEqual(ushort.MaxValue, rumble.LowFrequency);
        Assert.AreEqual((ushort)32896, rumble.HighFrequency);
    }

    [TestMethod]
    public void ViiperBoundaryPreservesTheMappedXboxReportExactly()
    {
        ControllerState state = new(
            ControllerButtons.South | ControllerButtons.RightShoulder,
            LeftX: short.MinValue,
            LeftY: short.MaxValue,
            RightX: 12345,
            RightY: -23456,
            LeftTrigger: 32767,
            RightTrigger: 16384);
        Xbox360Report expected = ControllerOutputMapping.ToXbox360Report(in state);

        global::Viiper.Client.Devices.Xbox360.Xbox360Input output =
            ViiperXbox360ControllerOutput.MapReport(expected);

        Assert.AreEqual((uint)expected.Buttons, output.Buttons);
        Assert.AreEqual(expected.LeftTrigger, output.Lt);
        Assert.AreEqual(expected.RightTrigger, output.Rt);
        Assert.AreEqual(expected.LeftX, output.Lx);
        Assert.AreEqual(expected.LeftY, output.Ly);
        Assert.AreEqual(expected.RightX, output.Rx);
        Assert.AreEqual(expected.RightY, output.Ry);
    }
}
