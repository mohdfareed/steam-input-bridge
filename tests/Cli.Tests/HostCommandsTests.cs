using System.CommandLine;

namespace Cli.Tests;

#pragma warning disable CA1416

/// <summary>Tests for host CLI helpers.</summary>
[TestClass]
public sealed class HostCommandsTests
{
    /// <summary>Checks host run parses without gamepad selection.</summary>
    [TestMethod]
    public void HostRunAcceptsNoOptions()
    {
        Command host = HostCommands.CreateHostCommand();
        ParseResult result = host.Parse("run");

        Assert.HasCount(0, result.Errors);
    }
}

#pragma warning restore CA1416
