using System.Linq;
using System.Runtime.Versioning;

namespace Cli.Tests;

/// <summary>Tests for test CLI helpers.</summary>
[SupportedOSPlatform("windows")]
[TestClass]
public sealed class TestCommandsTests
{
    /// <summary>Checks top-level test command shape.</summary>
    [TestMethod]
    public void CreateTestCommandIncludesExpectedGroups()
    {
        string[] names = [.. TestCommands.CreateTestCommand().Subcommands.Select(command => command.Name)];

        CollectionAssert.Contains(names, "host");
        CollectionAssert.Contains(names, "mouse");
        CollectionAssert.Contains(names, "xpad");
    }

    /// <summary>Checks host diagnostics command shape.</summary>
    [TestMethod]
    public void CreateTestCommandIncludesHostPipes()
    {
        System.CommandLine.Command test = TestCommands.CreateTestCommand();
        System.CommandLine.Command host = test.Subcommands.Single(command => command.Name == "host");

        CollectionAssert.Contains(host.Subcommands.Select(command => command.Name).ToArray(), "pipes");
    }

    /// <summary>Checks mouse test command shape.</summary>
    [TestMethod]
    public void CreateTestCommandIncludesMouseTools()
    {
        System.CommandLine.Command test = TestCommands.CreateTestCommand();
        System.CommandLine.Command mouse = test.Subcommands.Single(command => command.Name == "mouse");
        string[] names = [.. mouse.Subcommands.Select(command => command.Name)];

        CollectionAssert.Contains(names, "input");
        CollectionAssert.Contains(names, "nullify");
        CollectionAssert.Contains(names, "bench");
    }

    /// <summary>Checks xpad test command shape.</summary>
    [TestMethod]
    public void CreateTestCommandIncludesXpadTools()
    {
        System.CommandLine.Command test = TestCommands.CreateTestCommand();
        System.CommandLine.Command xpad = test.Subcommands.Single(command => command.Name == "xpad");
        string[] names = [.. xpad.Subcommands.Select(command => command.Name)];

        CollectionAssert.Contains(names, "probe");
        CollectionAssert.Contains(names, "input");
        CollectionAssert.Contains(names, "press");
        CollectionAssert.Contains(names, "bench");
    }

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
