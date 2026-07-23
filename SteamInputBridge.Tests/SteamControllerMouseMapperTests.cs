using System;
using SteamInputBridge.Forwarding.Mouse;
using SteamInputBridge.Inputs.Controller;
using SteamInputBridge.Inputs.Mouse;
using SteamInputBridge.Settings;

namespace SteamInputBridge.Tests;

[TestClass]
public sealed class SteamControllerMouseMapperTests
{
    private static readonly double DefaultSensitivity = new SteamInputBridgeSettings().MouseSensitivity;

    [TestMethod]
    public void MapsVirtualControllerButtonsAndShoulderEdges()
    {
        SteamControllerMouseMapper mapper = new(DefaultSensitivity);
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
        SteamControllerMouseMapper mapper = new(DefaultSensitivity);
        ControllerState full = ControllerState.Empty with { RightX = short.MaxValue, RightY = short.MinValue };

        Assert.IsFalse(mapper.TryMap(in full, TimeSpan.Zero, out _));
        Assert.IsTrue(mapper.TryMap(in full, TimeSpan.FromMilliseconds(4), out MouseReport report));
        Assert.AreEqual(16, report.DeltaX);
        Assert.AreEqual(-16, report.DeltaY);

        mapper.Reset();
        ControllerState fractional = ControllerState.Empty with { RightX = 1_024 };
        Assert.IsFalse(mapper.TryMap(in fractional, TimeSpan.Zero, out _));
        Assert.IsFalse(mapper.TryMap(in fractional, TimeSpan.FromMilliseconds(4), out _));
        Assert.IsTrue(mapper.TryMap(in fractional, TimeSpan.FromMilliseconds(4), out report));
        Assert.AreEqual(1, report.DeltaX);
    }

    [TestMethod]
    public void HeldStickContinuesMovingAcrossTicks()
    {
        SteamControllerMouseMapper mapper = new(DefaultSensitivity);
        ControllerState held = ControllerState.Empty with { RightX = short.MaxValue };

        Assert.IsFalse(mapper.TryMap(in held, TimeSpan.Zero, out _));
        Assert.IsTrue(mapper.TryMap(in held, TimeSpan.FromMilliseconds(4), out MouseReport first));
        Assert.IsTrue(mapper.TryMap(in held, TimeSpan.FromMilliseconds(4), out MouseReport second));
        Assert.AreEqual(16, first.DeltaX);
        Assert.AreEqual(16, second.DeltaX);
    }

    [TestMethod]
    public void StateChangeIntegratesThePreviouslyHeldValue()
    {
        SteamControllerMouseMapper mapper = new(DefaultSensitivity);
        ControllerState held = ControllerState.Empty with { RightX = short.MaxValue };
        ControllerState released = ControllerState.Empty;

        Assert.IsFalse(mapper.TryMap(in held, TimeSpan.Zero, out _));
        Assert.IsTrue(mapper.TryMap(in released, TimeSpan.FromMilliseconds(4), out MouseReport report));
        Assert.AreEqual(16, report.DeltaX);
        Assert.IsFalse(mapper.TryMap(in released, TimeSpan.FromMilliseconds(4), out _));
    }

    [TestMethod]
    public void UsesConfiguredSensitivity()
    {
        SteamControllerMouseMapper mapper = new(8_000.0);
        ControllerState held = ControllerState.Empty with { RightX = short.MaxValue };

        Assert.IsFalse(mapper.TryMap(in held, TimeSpan.Zero, out _));
        Assert.IsTrue(mapper.TryMap(in held, TimeSpan.FromMilliseconds(4), out MouseReport report));
        Assert.AreEqual(32, report.DeltaX);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(250)]
    [DataRow(500)]
    [DataRow(1000)]
    [DataRow(1600)]
    [DataRow(2000)]
    public void ConservesMovementAcrossInputRates(int updatesPerSecond)
    {
        SteamControllerMouseMapper mapper = new(DefaultSensitivity);
        ControllerState held = ControllerState.Empty with
        {
            RightX = short.MaxValue,
            RightY = short.MinValue,
        };
        TimeSpan elapsed = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / updatesPerSecond);
        int totalX = 0;
        int totalY = 0;

        Assert.IsFalse(mapper.TryMap(in held, TimeSpan.Zero, out _));
        for (int i = 0; i < updatesPerSecond; i++)
        {
            if (mapper.TryMap(in held, elapsed, out MouseReport report))
            {
                totalX += report.DeltaX;
                totalY += report.DeltaY;
            }
        }

        Assert.AreEqual((int)DefaultSensitivity, totalX, delta: 1);
        Assert.AreEqual(-(int)DefaultSensitivity, totalY, delta: 1);
    }

    [TestMethod]
    public void ReversalsConserveSignedMovementWithoutChangingScale()
    {
        SteamControllerMouseMapper mapper = new(DefaultSensitivity);
        ControllerState right = ControllerState.Empty with { RightX = short.MaxValue };
        ControllerState left = ControllerState.Empty with { RightX = short.MinValue };
        TimeSpan interval = TimeSpan.FromMilliseconds(1);
        int totalX = 0;

        Assert.IsFalse(mapper.TryMap(in right, TimeSpan.Zero, out _));
        for (int i = 0; i < 500; i++)
        {
            AddDelta(mapper, in right, interval, ref totalX);
        }

        AddDelta(mapper, in left, TimeSpan.Zero, ref totalX);
        for (int i = 0; i < 500; i++)
        {
            AddDelta(mapper, in left, interval, ref totalX);
        }

        Assert.AreEqual(0, totalX, delta: 1);
    }

    [TestMethod]
    public void IrregularUpdateIntervalsConserveMovementForTheSameElapsedTime()
    {
        SteamControllerMouseMapper mapper = new(DefaultSensitivity);
        ControllerState held = ControllerState.Empty with { RightX = short.MaxValue };
        long[] intervalTicks = [1, 12_345, 8_765_432, TimeSpan.TicksPerSecond - 8_777_778];
        int totalX = 0;

        Assert.IsFalse(mapper.TryMap(in held, TimeSpan.Zero, out _));
        foreach (long ticks in intervalTicks)
        {
            AddDelta(mapper, in held, TimeSpan.FromTicks(ticks), ref totalX);
        }

        Assert.AreEqual((int)DefaultSensitivity, totalX, delta: 1);
    }

    private static void AddDelta(
        SteamControllerMouseMapper mapper,
        in ControllerState state,
        TimeSpan elapsed,
        ref int totalX)
    {
        if (mapper.TryMap(in state, elapsed, out MouseReport report))
        {
            totalX += report.DeltaX;
        }
    }
}
