using System;
using Inputs.Sdl;

namespace Hosting.Tests;

/// <summary>Tests for host-level SDL motion selection.</summary>
[TestClass]
public sealed class SdlGamepadMotionSelectorTests
{
    /// <summary>Checks Steam Controller physical motion is selected only on a strict match.</summary>
    [TestMethod]
    public void ResolveOptionsSelectsStrictSteamControllerPhysicalMotionCounterpart()
    {
        SdlGamepadInfo steam = new(1, 2, "Steam Controller", 0x1234, 0x28de, 0x1302, null);
        SdlGamepadInfo physical = new(2, 3, "steam controller", 0, 0x28de, 0x1304, null)
        {
            HasGyro = true,
        };

        SdlGamepadOptions options = SdlGamepadMotionSelector.ResolveOptions(
            [steam, physical],
            new SdlGamepadOptions
            {
                DeviceIndex = 1,
            });

        Assert.AreEqual(2, options.MotionDeviceIndex);
    }

    /// <summary>Checks unrelated physical motion devices are ignored.</summary>
    [TestMethod]
    public void ResolveOptionsIgnoresUnmatchedPhysicalMotionDevice()
    {
        SdlGamepadInfo steam = new(1, 2, "Steam Controller", 0x1234, 0x28de, 0x1302, null);
        SdlGamepadInfo physical = new(2, 3, "DualSense", 0, 0x054c, 0x0df2, null)
        {
            HasGyro = true,
        };

        SdlGamepadOptions options = SdlGamepadMotionSelector.ResolveOptions(
            [steam, physical],
            new SdlGamepadOptions
            {
                DeviceIndex = 1,
            });

        Assert.IsNull(options.MotionDeviceIndex);
    }

    /// <summary>Checks explicit motion override is validated.</summary>
    [TestMethod]
    public void ResolveOptionsRejectsExplicitDeviceWithoutMotion()
    {
        SdlGamepadInfo primary = new(1, 2, "Controller", 0, 0x045e, 0x028e, null);
        SdlGamepadInfo motion = new(2, 3, "No Motion", 0, 0x045e, 0x028e, null);

        InvalidOperationException exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
            SdlGamepadMotionSelector.ResolveOptions(
                [primary, motion],
                new SdlGamepadOptions
                {
                    DeviceIndex = 1,
                    MotionDeviceIndex = 2,
                }));

        StringAssert.Contains(exception.Message, "does not expose", System.StringComparison.Ordinal);
    }
}
