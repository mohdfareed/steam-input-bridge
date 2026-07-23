using SteamInputBridge.Inputs.RawInput;

namespace SteamInputBridge.Tests;

[TestClass]
public sealed class RawInputMouseSourceTests
{
    private const ushort VerticalWheel = 0x0400;

    [TestMethod]
    public void VerticalWheelAccumulatesPartialDeltasIntoWholeSteps()
    {
        RawInputMouseSource.VerticalWheelAccumulator accumulator = new();

        Assert.AreEqual(0, accumulator.Accumulate(1, VerticalWheel, Encode(40)));
        Assert.AreEqual(0, accumulator.Accumulate(1, VerticalWheel, Encode(40)));
        Assert.AreEqual(1, accumulator.Accumulate(1, VerticalWheel, Encode(40)));
        Assert.AreEqual(2, accumulator.Accumulate(1, VerticalWheel, Encode(300)));
        Assert.AreEqual(1, accumulator.Accumulate(1, VerticalWheel, Encode(60)));
        Assert.AreEqual(-2, accumulator.Accumulate(1, VerticalWheel, Encode(-300)));
        Assert.AreEqual(-1, accumulator.Accumulate(1, VerticalWheel, Encode(-60)));
    }

    [TestMethod]
    public void VerticalWheelOppositePartialDeltasCancelWithoutResidue()
    {
        RawInputMouseSource.VerticalWheelAccumulator accumulator = new();

        Assert.AreEqual(0, accumulator.Accumulate(1, VerticalWheel, Encode(90)));
        Assert.AreEqual(0, accumulator.Accumulate(1, VerticalWheel, Encode(-30)));
        Assert.AreEqual(0, accumulator.Accumulate(1, VerticalWheel, Encode(-60)));
        Assert.AreEqual(1, accumulator.Accumulate(1, VerticalWheel, Encode(120)));
    }

    [TestMethod]
    public void VerticalWheelTracksRemaindersPerDevice()
    {
        RawInputMouseSource.VerticalWheelAccumulator accumulator = new();

        Assert.AreEqual(0, accumulator.Accumulate(1, VerticalWheel, Encode(60)));
        Assert.AreEqual(0, accumulator.Accumulate(2, VerticalWheel, Encode(70)));
        Assert.AreEqual(1, accumulator.Accumulate(1, VerticalWheel, Encode(60)));
        Assert.AreEqual(1, accumulator.Accumulate(2, VerticalWheel, Encode(50)));
    }

    private static ushort Encode(short delta)
    {
        return unchecked((ushort)delta);
    }
}
