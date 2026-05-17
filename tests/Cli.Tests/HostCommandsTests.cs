using System.CommandLine;
using System.Linq;

namespace Cli.Tests;

#pragma warning disable CA1416

/// <summary>Tests for host CLI helpers.</summary>
[TestClass]
public sealed class HostCommandsTests
{
    /// <summary>Checks CLI host command shape.</summary>
    [TestMethod]
    public void CreateHostCommandIncludesControlCommands()
    {
        string[] names = [.. HostCommands.CreateHostCommand().Subcommands.Select(command => command.Name)];

        CollectionAssert.Contains(names, "run");
        CollectionAssert.Contains(names, "status");
        CollectionAssert.Contains(names, "stop");
    }

    /// <summary>Checks route options.</summary>
    [TestMethod]
    public void CreateHostCommandIncludesExpectedOptions()
    {
        System.CommandLine.Command host = HostCommands.CreateHostCommand();
        System.CommandLine.Command run = host.Subcommands.Single(command => command.Name == "run");
        System.CommandLine.Command status = host.Subcommands.Single(command => command.Name == "status");
        System.CommandLine.Command stop = host.Subcommands.Single(command => command.Name == "stop");

        CollectionAssert.Contains(run.Options.Select(option => option.Name).ToArray(), "--xpad-device-index");
        CollectionAssert.Contains(run.Options.Select(option => option.Name).ToArray(), "--xpad-poll-ms");
        Assert.HasCount(0, status.Options);
        Assert.HasCount(0, stop.Options);
    }

    /// <summary>Checks host run xpad options.</summary>
    [TestMethod]
    public void CreateHostCommandRunIncludesSdlOptions()
    {
        System.CommandLine.Command host = HostCommands.CreateHostCommand();
        System.CommandLine.Command run = host.Subcommands.Single(command => command.Name == "run");
        string[] names = [.. run.Options.Select(option => option.Name)];

        CollectionAssert.Contains(names, "--xpad-device-index");
        CollectionAssert.Contains(names, "--xpad-poll-ms");
    }

    /// <summary>Checks host run xpad selection parses.</summary>
    [TestMethod]
    public void HostRunAcceptsXpadDeviceIndex()
    {
        Command host = HostCommands.CreateHostCommand();
        ParseResult result = host.Parse("run --xpad-device-index 1");

        Assert.HasCount(0, result.Errors);
    }
}

#pragma warning restore CA1416
