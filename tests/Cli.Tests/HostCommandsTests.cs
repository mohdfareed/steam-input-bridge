using System.CommandLine;

namespace Cli.Tests;

#pragma warning disable CA1416

/// <summary>Tests for host CLI helpers.</summary>
[TestClass]
public sealed class HostCommandsTests
{
    /// <summary>Checks host run xpad selection parses.</summary>
    [TestMethod]
    public void HostRunAcceptsXpadDeviceIndex()
    {
        Command host = HostCommands.CreateHostCommand();
        ParseResult result = host.Parse("run --xpad-device-index 1");

        Assert.HasCount(0, result.Errors);
    }

    /// <summary>Checks host run xpad motion selection parses.</summary>
    [TestMethod]
    public void HostRunAcceptsXpadMotionDeviceIndex()
    {
        Command host = HostCommands.CreateHostCommand();
        ParseResult result = host.Parse("run --xpad-motion-device-index 2");

        Assert.HasCount(0, result.Errors);
    }
}

#pragma warning restore CA1416
