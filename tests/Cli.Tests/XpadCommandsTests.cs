namespace Cli.Tests;

/// <summary>Tests for xpad CLI helpers.</summary>
[TestClass]
public sealed class XpadCommandsTests
{
    /// <summary>Checks empty button formatting.</summary>
    [TestMethod]
    public void DisplayButtonsReturnsNoneForEmptyButtons()
    {
        Assert.AreEqual("none", XpadCommands.DisplayButtons(GamepadButtons.None));
    }

    /// <summary>Checks motion formatting.</summary>
    [TestMethod]
    public void DisplayMotionShowsGyroAndAccelerometer()
    {
        GamepadMotion motion = new(true, 1.25f, -2.5f, 3, true, 4, 5.125f, -6);

        Assert.AreEqual(
            "gyro=1.25,-2.5,3 accel=4,5.125,-6",
            XpadCommands.DisplayMotion(motion));
    }
}
