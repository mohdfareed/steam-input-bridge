using System;
using SteamInputBridge.Forwarding.Mouse;
using SteamInputBridge.Inputs.Controller;
using SteamInputBridge.Inputs.Mouse;

namespace SteamInputBridge.Tests;

[TestClass]
public sealed class SteamControllerMouseMapperTests
{
    [TestMethod]
    public void MapsVirtualControllerButtonsAndShoulderEdges()
    {
        SteamControllerMouseMapper mapper = new();
        ControllerState pressed = new(
            ControllerButtons.RightStick | ControllerButtons.RightShoulder,
            0,
            0,
            0,
            0,
            LeftTrigger: 1,
            RightTrigger: 1);

        Assert.IsTrue(mapper.TryMap(in pressed, TimeSpan.Zero, out MouseReport report));
        Assert.AreEqual(MouseButtons.Left | MouseButtons.Right | MouseButtons.Middle, report.Buttons);
        Assert.AreEqual(-1, report.WheelDelta);
        Assert.IsFalse(mapper.TryMap(in pressed, TimeSpan.Zero, out _));

        ControllerState released = ControllerState.Empty;
        Assert.IsTrue(mapper.TryMap(in released, TimeSpan.Zero, out report));
        Assert.AreEqual(MouseButtons.None, report.Buttons);

        ControllerState scrollUp = released with { Buttons = ControllerButtons.LeftShoulder };
        Assert.IsTrue(mapper.TryMap(in scrollUp, TimeSpan.Zero, out report));
        Assert.AreEqual(1, report.WheelDelta);
    }

    [TestMethod]
    public void MapsStickLinearlyAndRetainsFractionalMovement()
    {
        SteamControllerMouseMapper mapper = new();
        ControllerState full = ControllerState.Empty with { RightX = short.MaxValue, RightY = short.MinValue };

        Assert.IsTrue(mapper.TryMap(in full, TimeSpan.FromMilliseconds(4), out MouseReport report));
        Assert.AreEqual(16, report.DeltaX);
        Assert.AreEqual(-16, report.DeltaY);

        mapper.Reset();
        ControllerState fractional = ControllerState.Empty with { RightX = 1_024 };
        Assert.IsFalse(mapper.TryMap(in fractional, TimeSpan.FromMilliseconds(4), out _));
        Assert.IsTrue(mapper.TryMap(in fractional, TimeSpan.FromMilliseconds(4), out report));
        Assert.AreEqual(1, report.DeltaX);
    }

    [TestMethod]
    public void HeldStickContinuesMovingAcrossTicks()
    {
        SteamControllerMouseMapper mapper = new();
        ControllerState held = ControllerState.Empty with { RightX = short.MaxValue };

        Assert.IsTrue(mapper.TryMap(in held, TimeSpan.FromMilliseconds(4), out MouseReport first));
        Assert.IsTrue(mapper.TryMap(in held, TimeSpan.FromMilliseconds(4), out MouseReport second));
        Assert.AreEqual(16, first.DeltaX);
        Assert.AreEqual(16, second.DeltaX);
    }
}
