using System.CommandLine;

namespace Cli.Tests;

/// <summary>Tests for client CLI helpers.</summary>
[TestClass]
public sealed class ClientCommandsTests
{
    /// <summary>Checks client run accepts a route-less session.</summary>
    [TestMethod]
    public void ClientRunAcceptsRouteLessSession()
    {
        ParseResult result = ClientCommands.CreateClientCommand().Parse("run");

        Assert.HasCount(0, result.Errors);
    }

    /// <summary>Checks client run accepts a mouse session.</summary>
    [TestMethod]
    public void ClientRunAcceptsMouseSession()
    {
        ParseResult result = ClientCommands.CreateClientCommand().Parse("run --mouse");

        Assert.HasCount(0, result.Errors);
    }
}
