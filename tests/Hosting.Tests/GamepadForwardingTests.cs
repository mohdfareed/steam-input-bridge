namespace Hosting.Tests;

/// <summary>Tests gamepad report mapping.</summary>
[TestClass]
public sealed class GamepadForwardingTests
{
    /// <summary>Checks standard buttons map to Xbox buttons.</summary>
    [TestMethod]
    public void ToXbox360ReportMapsButtons()
    {
        GamepadState state = new(
            GamepadButtons.South | GamepadButtons.East | GamepadButtons.LeftShoulder | GamepadButtons.DPadUp,
            0,
            0,
            0,
            0,
            0,
            0,
            default);

        Xbox360Report report = GamepadForwardingExtensions.ToXbox360Report(state);

        Assert.AreEqual(
            Xbox360Buttons.A | Xbox360Buttons.B | Xbox360Buttons.LeftShoulder | Xbox360Buttons.DPadUp,
            report.Buttons);
    }

    /// <summary>Checks trigger conversion.</summary>
    [TestMethod]
    public void ToXbox360ReportScalesTriggersToBytes()
    {
        GamepadState state = new(GamepadButtons.None, 0, 0, 0, 0, 32767, 0, default);

        Xbox360Report report = GamepadForwardingExtensions.ToXbox360Report(state);

        Assert.AreEqual(byte.MaxValue, report.LeftTrigger);
        Assert.AreEqual(byte.MinValue, report.RightTrigger);
    }

    /// <summary>Checks Y axis inversion for Xbox output.</summary>
    [TestMethod]
    public void ToXbox360ReportInvertsYAxis()
    {
        GamepadState state = new(GamepadButtons.None, 1, -2, 3, 4, 0, 0, default);

        Xbox360Report report = GamepadForwardingExtensions.ToXbox360Report(state);

        Assert.AreEqual((short)1, report.LeftX);
        Assert.AreEqual((short)2, report.LeftY);
        Assert.AreEqual((short)3, report.RightX);
        Assert.AreEqual((short)-4, report.RightY);
    }

    /// <summary>Checks Xbox rumble feedback maps to gamepad rumble.</summary>
    [TestMethod]
    public void ToGamepadRumbleScalesMotors()
    {
        GamepadRumble rumble = GamepadForwardingExtensions.ToGamepadRumble(new Xbox360Rumble(1, 2));

        Assert.AreEqual((ushort)257, rumble.LowFrequency);
        Assert.AreEqual((ushort)514, rumble.HighFrequency);
    }
}
