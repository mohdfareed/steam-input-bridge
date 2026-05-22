using System.IO;
using System.Threading.Tasks;
using SteamInputBridge.Forwarding;
using SteamInputBridge.Forwarding.Controller;

namespace SteamInputBridge.Tests;

/// <summary>Tests controller hot-path pipe frames.</summary>
[TestClass]
public sealed class ControllerPipeTests
{
    /// <summary>Checks input frame round trips through the binary stream codec.</summary>
    [TestMethod]
    public async Task InputFrameRoundTrips()
    {
        using MemoryStream stream = new();
        ControllerPipeWriter writer = new(stream);
        ControllerInputFrame input = new(
            7,
            new ControllerState(
                new ControllerStandardState(ControllerButtons.South, 1, 2, 3, 4, 5, 6),
                new ControllerMotionState(true, 1.5f, 2.5f, 3.5f, true, 4.5f, 5.5f, 6.5f),
                new ControllerTouchpadState(true, 0.25f, 0.75f)));

        await writer.WriteInputAsync(input).ConfigureAwait(false);
        stream.Position = 0;
        ControllerPipeMessage message = await new ControllerPipeReader(stream).ReadAsync().ConfigureAwait(false);

        Assert.AreEqual(ControllerPipeFrameType.Input, message.Type);
        Assert.AreEqual((ushort)7, message.Input.ControllerIndex);
        Assert.AreEqual(ControllerButtons.South, message.Input.State.Standard?.Buttons);
        Assert.AreEqual(1.5f, message.Input.State.Motion?.GyroX);
        Assert.AreEqual(0.25f, message.Input.State.Touchpad?.X);
    }

    /// <summary>Checks feedback frame round trips through the binary stream codec.</summary>
    [TestMethod]
    public async Task FeedbackFrameRoundTrips()
    {
        using MemoryStream stream = new();
        ControllerPipeWriter writer = new(stream);
        ControllerFeedbackFrame feedback = new(
            3,
            new ControllerFeedback(
                new ControllerRumble(10, 20),
                new ControllerLight(1, 2, 3),
                new ControllerAdaptiveTriggers(4, 5)));

        await writer.WriteFeedbackAsync(feedback).ConfigureAwait(false);
        stream.Position = 0;
        ControllerPipeMessage message = await new ControllerPipeReader(stream).ReadAsync().ConfigureAwait(false);

        Assert.AreEqual(ControllerPipeFrameType.Feedback, message.Type);
        Assert.AreEqual((ushort)3, message.Feedback.ControllerIndex);
        Assert.AreEqual((ushort)10, message.Feedback.Feedback.Rumble?.LowFrequency);
        Assert.AreEqual((byte)2, message.Feedback.Feedback.Light?.Green);
        Assert.AreEqual((byte)5, message.Feedback.Feedback.AdaptiveTriggers?.RightMode);
    }
}
