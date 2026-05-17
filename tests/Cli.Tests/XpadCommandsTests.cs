using System.Linq;
namespace Cli.Tests;

/// <summary>Tests for xpad CLI helpers.</summary>
[TestClass]
public sealed class XpadCommandsTests
{
    /// <summary>Checks xpad command shape.</summary>
    [TestMethod]
    public void XpadHelperCommandsIncludeExpectedOptions()
    {
        System.CommandLine.Command input = XpadCommands.CreateInputCommand();
        System.CommandLine.Command press = XpadCommands.CreatePressCommand();
        string[] inputOptionNames = [.. input.Options.Select(option => option.Name)];

        CollectionAssert.Contains(inputOptionNames, "--device-index");
        CollectionAssert.Contains(inputOptionNames, "--poll-ms");
        CollectionAssert.Contains(press.Options.Select(option => option.Name).ToArray(), "--duration-ms");
    }

    /// <summary>Checks empty button formatting.</summary>
    [TestMethod]
    public void DisplayButtonsReturnsNoneForEmptyButtons()
    {
        Assert.AreEqual("none", XpadCommands.DisplayButtons(GamepadButtons.None));
    }
}
