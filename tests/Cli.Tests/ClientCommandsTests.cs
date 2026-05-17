using System.CommandLine;
using System.Linq;

namespace Cli.Tests;

/// <summary>Tests for client CLI helpers.</summary>
[TestClass]
public sealed class ClientCommandsTests
{
    /// <summary>Checks client command shape.</summary>
    [TestMethod]
    public void CreateClientCommandIncludesExpectedCommands()
    {
        string[] names = [.. ClientCommands.CreateClientCommand().Subcommands.Select(command => command.Name)];

        CollectionAssert.Contains(names, "run");
    }

    /// <summary>Checks client route option wiring.</summary>
    [TestMethod]
    public void CreateClientCommandIncludesRouteOption()
    {
        Command client = ClientCommands.CreateClientCommand();
        Command run = client.Subcommands.Single(command => command.Name == "run");

        CollectionAssert.Contains(run.Options.Select(option => option.Name).ToArray(), "--route");
    }
}
