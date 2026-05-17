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

    /// <summary>Checks client run accepts a route session.</summary>
    [TestMethod]
    public void ClientRunAcceptsRouteSession()
    {
        ParseResult result = ClientCommands.CreateClientCommand().Parse("run --route mouse");

        Assert.HasCount(0, result.Errors);
    }

    /// <summary>Checks client run rejects unknown routes.</summary>
    [TestMethod]
    public void ClientRunRejectsUnknownRoute()
    {
        ParseResult result = ClientCommands.CreateClientCommand().Parse("run --route nope");

        Assert.AreNotEqual(0, result.Errors.Count);
    }
}
