using System.Linq;
using SteamInput;

namespace Cli.Tests;

/// <summary>Tests for Steam CLI helpers.</summary>
[TestClass]
public sealed class SteamCommandsTests
{
    /// <summary>Checks Steam command shape.</summary>
    [TestMethod]
    public void CreateSteamCommandIncludesControlCommands()
    {
        string[] names = [.. SteamCommands.CreateSteamCommand().Subcommands.Select(command => command.Name)];

        CollectionAssert.Contains(names, "list");
        CollectionAssert.Contains(names, "force");
        CollectionAssert.Contains(names, "clear");
        CollectionAssert.Contains(names, "open-config");
    }

    /// <summary>Checks Steam command option and argument wiring.</summary>
    [TestMethod]
    public void CreateSteamCommandIncludesExpectedInputs()
    {
        System.CommandLine.Command steam = SteamCommands.CreateSteamCommand();
        System.CommandLine.Command list = steam.Subcommands.Single(command => command.Name == "list");
        System.CommandLine.Command force = steam.Subcommands.Single(command => command.Name == "force");
        System.CommandLine.Command openConfig = steam.Subcommands.Single(command => command.Name == "open-config");
        string[] listOptionNames = [.. list.Options.Select(option => option.Name)];

        CollectionAssert.Contains(listOptionNames, "--steam-path");
        CollectionAssert.Contains(listOptionNames, "--user-id");
        CollectionAssert.Contains(force.Arguments.Select(argument => argument.Name).ToArray(), "app-id");
        CollectionAssert.Contains(openConfig.Arguments.Select(argument => argument.Name).ToArray(), "app-id");
    }

    /// <summary>Checks Steam game kind formatting.</summary>
    [TestMethod]
    public void DisplayKindFormatsShortcut()
    {
        Assert.AreEqual("shortcut", SteamCommands.DisplayKind(SteamGameKind.NonSteamShortcut));
    }
}
