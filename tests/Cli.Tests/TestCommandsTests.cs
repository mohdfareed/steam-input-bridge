namespace Cli.Tests;

/// <summary>Tests for test CLI helpers.</summary>
[TestClass]
public sealed class TestCommandsTests
{
    /// <summary>Checks empty button formatting.</summary>
    [TestMethod]
    public void MouseDisplayButtonsReturnsNoneForEmptyButtons()
    {
        Assert.AreEqual("none", InputCommands.DisplayButtons(MouseButtons.None));
    }

    /// <summary>Checks non-empty button formatting.</summary>
    [TestMethod]
    public void MouseDisplayButtonsReturnsNamedButtons()
    {
        Assert.AreEqual(
            "Left, Right",
            InputCommands.DisplayButtons(MouseButtons.Left | MouseButtons.Right));
    }
}
