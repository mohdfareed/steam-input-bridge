using System;
using Inputs.Sdl;

namespace Inputs.Tests;

/// <summary>Tests for SDL gamepad helpers.</summary>
[TestClass]
public sealed class SdlGamepadSourceTests
{
    /// <summary>Checks SDL trigger conversion clamps negative values.</summary>
    [TestMethod]
    public void ToTriggerClampsNegativeValuesToZero()
    {
        Assert.AreEqual((ushort)0, SdlGamepadSource.ToTrigger(-1));
        Assert.AreEqual((ushort)32767, SdlGamepadSource.ToTrigger(32767));
    }

    /// <summary>Checks primary motion is used when available.</summary>
    [TestMethod]
    public void ResolveMotionDeviceIndexUsesPrimaryMotionWhenAvailable()
    {
        SdlGamepadInfo primary = new(1, 2, "DualSense", 0, 0x054c, 0x0df2, null) { HasGyro = true };

        int index = SdlGamepadSource.ResolveMotionDeviceIndex(
            [primary],
            primary,
            new SdlGamepadOptions());

        Assert.AreEqual(1, index);
    }

    /// <summary>Checks missing motion source fails.</summary>
    [TestMethod]
    public void ResolveMotionDeviceIndexRejectsMissingMotionSource()
    {
        SdlGamepadInfo primary = new(1, 2, "Steam Controller", 0x1234, 0x045e, 0x028e, null);
        SdlGamepadInfo physical = new(0, 1, "DualSense", 0, 0x054c, 0x0df2, null) { HasGyro = true };

        InvalidOperationException exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
            SdlGamepadSource.ResolveMotionDeviceIndex(
                [physical, primary],
                primary,
                new SdlGamepadOptions()));

        StringAssert.Contains(exception.Message, "No matching", StringComparison.Ordinal);
    }

    /// <summary>Checks explicit motion index wins when the device exists.</summary>
    [TestMethod]
    public void ResolveMotionDeviceIndexUsesExplicitIndex()
    {
        SdlGamepadInfo primary = new(1, 2, "Steam Controller", 0x1234, 0x045e, 0x028e, null);
        SdlGamepadInfo motion = new(5, 3, "Motion Controller", 0, 0x045e, 0x028e, null) { HasGyro = true };

        int index = SdlGamepadSource.ResolveMotionDeviceIndex(
            [primary, motion],
            primary,
            new SdlGamepadOptions
            {
                MotionDeviceIndex = 5,
            });

        Assert.AreEqual(5, index);
    }

    /// <summary>Checks missing explicit motion index fails.</summary>
    [TestMethod]
    public void ResolveMotionDeviceIndexRejectsMissingExplicitIndex()
    {
        SdlGamepadInfo primary = new(1, 2, "Steam Controller", 0x1234, 0x045e, 0x028e, null);

        ArgumentOutOfRangeException exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            SdlGamepadSource.ResolveMotionDeviceIndex(
                [primary],
                primary,
                new SdlGamepadOptions
                {
                    MotionDeviceIndex = 5,
                }));

        Assert.AreEqual("deviceIndex", exception.ParamName);
    }

    /// <summary>Checks motion events come from the configured motion source.</summary>
    [TestMethod]
    public void IsMotionEventUsesConfiguredMotionInstance()
    {
        Assert.IsTrue(SdlGamepadSource.IsMotionEvent(1, primaryInstanceId: 1, motionInstanceId: null));
        Assert.IsFalse(SdlGamepadSource.IsMotionEvent(2, primaryInstanceId: 1, motionInstanceId: null));
        Assert.IsTrue(SdlGamepadSource.IsMotionEvent(2, primaryInstanceId: 1, motionInstanceId: 2));
        Assert.IsFalse(SdlGamepadSource.IsMotionEvent(1, primaryInstanceId: 1, motionInstanceId: 2));
    }

    /// <summary>Checks motion construction uses sensor capability flags.</summary>
    [TestMethod]
    public void CreateMotionUsesAvailableSensors()
    {
        GamepadMotion motion = SdlGamepadSource.CreateMotion(
            hasGyro: true,
            [1, 2, 3],
            hasAccelerometer: false,
            [4, 5, 6]);

        Assert.IsTrue(motion.HasGyro);
        Assert.AreEqual(1, motion.GyroX);
        Assert.AreEqual(2, motion.GyroY);
        Assert.AreEqual(3, motion.GyroZ);
        Assert.IsFalse(motion.HasAccelerometer);
        Assert.AreEqual(0, motion.AccelX);
        Assert.AreEqual(0, motion.AccelY);
        Assert.AreEqual(0, motion.AccelZ);
    }
}
