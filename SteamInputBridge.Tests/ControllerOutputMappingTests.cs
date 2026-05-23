using System;
using SteamInputBridge.Forwarding.Controller;
using SteamInputBridge.Outputs.Viiper;

namespace SteamInputBridge.Tests;

/// <summary>Tests concrete controller report mapping.</summary>
[TestClass]
public sealed class ControllerOutputMappingTests
{
    /// <summary>Maps standard controller controls to Xbox 360 report fields.</summary>
    [TestMethod]
    public void MapsStandardStateToXbox360Report()
    {
        ControllerButtons buttons =
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
            ControllerButtons.DPadRight;
        ControllerState state = new(
            new ControllerStandardState(buttons, 10, -20, 30, short.MinValue, 32767, 16384),
            null,
            null);

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
        Assert.AreEqual(255, report.LeftTrigger);
        Assert.AreEqual(127, report.RightTrigger);
        Assert.AreEqual(10, report.LeftX);
        Assert.AreEqual(20, report.LeftY);
        Assert.AreEqual(30, report.RightX);
        Assert.AreEqual(short.MaxValue, report.RightY);
    }

    /// <summary>Missing standard controls map to a centered Xbox report.</summary>
    [TestMethod]
    public void MissingStandardStateMapsToEmptyXbox360Report()
    {
        Xbox360Report report = ControllerOutputMapping.ToXbox360Report(ControllerState.Empty);

        Assert.IsTrue(report.IsEmpty);
    }

    /// <summary>Maps standard and motion controller state to DS4 report fields.</summary>
    [TestMethod]
    public void MapsStateToDs4Report()
    {
        ControllerButtons buttons =
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
            ControllerButtons.DPadRight |
            ControllerButtons.TouchpadClick;
        ControllerState state = new(
            new ControllerStandardState(buttons, short.MaxValue, short.MinValue, 16384, -16384, 32767, 16384),
            new ControllerMotionState(
                true,
                MathF.PI,
                -MathF.PI / 2,
                0,
                true,
                9.81f,
                0,
                -9.81f),
            new ControllerTouchpadState(
                new ControllerTouchContact(true, 0.25f, 0.75f, 0.5f),
                new ControllerTouchContact(true, 1f, 0f, 1f)));

        Ds4Report report = ControllerOutputMapping.ToDs4Report(in state);

        Ds4Buttons expected =
            Ds4Buttons.Cross |
            Ds4Buttons.Circle |
            Ds4Buttons.Square |
            Ds4Buttons.Triangle |
            Ds4Buttons.Share |
            Ds4Buttons.PlayStation |
            Ds4Buttons.Options |
            Ds4Buttons.L3 |
            Ds4Buttons.R3 |
            Ds4Buttons.L1 |
            Ds4Buttons.R1 |
            Ds4Buttons.TouchpadClick |
            Ds4Buttons.L2 |
            Ds4Buttons.R2;
        Assert.AreEqual(expected, report.Buttons);
        Assert.AreEqual(Ds4DPad.UpRight, report.DPad);
        Assert.AreEqual((sbyte)127, report.LeftX);
        Assert.AreEqual(sbyte.MinValue, report.LeftY);
        Assert.AreEqual((sbyte)63, report.RightX);
        Assert.AreEqual((sbyte)-64, report.RightY);
        Assert.AreEqual(byte.MaxValue, report.LeftTrigger);
        Assert.AreEqual((byte)127, report.RightTrigger);
        Assert.AreEqual((ushort)480, report.Touch1X);
        Assert.AreEqual((ushort)706, report.Touch1Y);
        Assert.IsTrue(report.Touch1Active);
        Assert.AreEqual(Ds4Report.TouchpadMaxX, report.Touch2X);
        Assert.AreEqual((ushort)0, report.Touch2Y);
        Assert.IsTrue(report.Touch2Active);
        Assert.AreEqual((short)2880, report.GyroX);
        Assert.AreEqual((short)-1440, report.GyroY);
        Assert.AreEqual((short)0, report.GyroZ);
        Assert.AreEqual((short)5023, report.AccelX);
        Assert.AreEqual((short)0, report.AccelY);
        Assert.AreEqual((short)-5023, report.AccelZ);
    }

    /// <summary>Missing controller state maps to a neutral DS4 report.</summary>
    [TestMethod]
    public void MissingStateMapsToEmptyDs4Report()
    {
        Ds4Report report = ControllerOutputMapping.ToDs4Report(ControllerState.Empty);

        Assert.IsTrue(report.IsEmpty);
        Assert.AreEqual(Ds4DPad.Neutral, report.DPad);
        Assert.AreEqual(Ds4Report.DefaultAccelZ, report.AccelZ);
    }

    /// <summary>Maps Xbox rumble bytes to canonical feedback intensities.</summary>
    [TestMethod]
    public void MapsXbox360RumbleToControllerFeedback()
    {
        ControllerFeedback feedback = ControllerOutputMapping.ToControllerFeedback(
            new Xbox360Rumble(byte.MaxValue, 128));

        Assert.AreEqual(ushort.MaxValue, feedback.Rumble?.LowFrequency);
        Assert.AreEqual((ushort)32896, feedback.Rumble?.HighFrequency);
    }

    /// <summary>Maps DS4 output feedback to canonical feedback state.</summary>
    [TestMethod]
    public void MapsDs4FeedbackToControllerFeedback()
    {
        ControllerFeedback feedback = ControllerOutputMapping.ToControllerFeedback(
            new Ds4Feedback(
                SmallRumble: byte.MaxValue,
                LargeRumble: 128,
                LedRed: 10,
                LedGreen: 20,
                LedBlue: 30,
                FlashOn: 40,
                FlashOff: 50));

        Assert.AreEqual((ushort)32896, feedback.Rumble?.LowFrequency);
        Assert.AreEqual(ushort.MaxValue, feedback.Rumble?.HighFrequency);
        Assert.AreEqual((byte)10, feedback.Light?.Red);
        Assert.AreEqual((byte)20, feedback.Light?.Green);
        Assert.AreEqual((byte)30, feedback.Light?.Blue);
        Assert.AreEqual((byte)40, feedback.Light?.FlashOn);
        Assert.AreEqual((byte)50, feedback.Light?.FlashOff);
    }

    /// <summary>Maps the canonical DS4 report into VIIPER's generated DS4 input type.</summary>
    [TestMethod]
    public void MapsDs4ReportToViiperInput()
    {
        Ds4Report report = new(
            Ds4Buttons.Cross | Ds4Buttons.L2,
            Ds4DPad.DownLeft,
            1,
            2,
            3,
            4,
            5,
            6,
            7,
            8,
            true,
            9,
            10,
            false,
            11,
            12,
            13,
            14,
            15,
            16);

        global::Viiper.Client.Devices.Dualshock4.Dualshock4Input input =
            ViiperDs4Output.MapReport(report);

        Assert.AreEqual((ushort)(Ds4Buttons.Cross | Ds4Buttons.L2), input.Buttons);
        Assert.AreEqual((byte)Ds4DPad.DownLeft, input.Dpad);
        Assert.AreEqual((sbyte)1, input.Sticklx);
        Assert.AreEqual((sbyte)2, input.Stickly);
        Assert.AreEqual((sbyte)3, input.Stickrx);
        Assert.AreEqual((sbyte)4, input.Stickry);
        Assert.AreEqual((byte)5, input.Triggerl2);
        Assert.AreEqual((byte)6, input.Triggerr2);
        Assert.AreEqual((ushort)7, input.Touch1x);
        Assert.AreEqual((ushort)8, input.Touch1y);
        Assert.AreEqual((byte)1, input.Touch1active);
        Assert.AreEqual((ushort)9, input.Touch2x);
        Assert.AreEqual((ushort)10, input.Touch2y);
        Assert.AreEqual((byte)0, input.Touch2active);
        Assert.AreEqual((short)11, input.Gyrox);
        Assert.AreEqual((short)12, input.Gyroy);
        Assert.AreEqual((short)13, input.Gyroz);
        Assert.AreEqual((short)14, input.Accelx);
        Assert.AreEqual((short)15, input.Accely);
        Assert.AreEqual((short)16, input.Accelz);
    }
}
