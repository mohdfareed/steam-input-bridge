using System;
using System.CommandLine;
using Inputs.Sdl;

namespace Cli.Tests;

/// <summary>Tests for shared CLI option helpers.</summary>
[TestClass]
public sealed class CliOptionsTests
{
    /// <summary>Checks positive integer option validation.</summary>
    [TestMethod]
    public void CountOptionRejectsZero()
    {
        Command command = new("test");
        Option<int?> option = CliOptions.CreateCountOption(100);
        command.Options.Add(option);

        ParseResult result = command.Parse("--count 0");

        Assert.AreNotEqual(0, result.Errors.Count);
    }

    /// <summary>Checks app id argument validation.</summary>
    [TestMethod]
    public void AppIdArgumentRejectsZero()
    {
        Command command = new("test");
        Argument<uint> argument = CliOptions.CreateAppIdArgument();
        command.Arguments.Add(argument);

        ParseResult result = command.Parse("0");

        Assert.AreNotEqual(0, result.Errors.Count);
    }

    /// <summary>Checks SDL option projection.</summary>
    [TestMethod]
    public void CreateSdlGamepadOptionsReadsValues()
    {
        Command command = new("test");
        Option<int?> deviceIndexOption = CliOptions.CreateDeviceIndexOption("--device-index", "device");
        Option<int?> pollMsOption = CliOptions.CreatePollMsOption("--poll-ms", "poll");
        command.Options.Add(deviceIndexOption);
        command.Options.Add(pollMsOption);

        ParseResult result = command.Parse("--device-index 2 --poll-ms 5");
        SdlGamepadOptions options = CliOptions.CreateSdlGamepadOptions(
            result,
            deviceIndexOption,
            pollMsOption);

        Assert.AreEqual(2, options.DeviceIndex);
        Assert.AreEqual(TimeSpan.FromMilliseconds(5), options.PollInterval);
    }
}
