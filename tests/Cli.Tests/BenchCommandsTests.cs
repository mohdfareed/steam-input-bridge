using System.CommandLine;
using Cli.Tools.Benchmarks;

namespace Cli.Tests;

/// <summary>Tests for benchmark CLI helpers.</summary>
[TestClass]
public sealed class BenchCommandsTests
{
    /// <summary>Checks benchmark command parses an explicit input/output pair.</summary>
    [TestMethod]
    public void BenchCommandAcceptsInputAndOutput()
    {
        Command command = BenchCommands.CreateBenchCommand();
        ParseResult result = command.Parse("raw viiper --count 10");

        Assert.HasCount(0, result.Errors);
    }

    /// <summary>Checks fixed-input benchmark command parses only output.</summary>
    [TestMethod]
    public void BenchCommandWithFixedInputAcceptsOutput()
    {
        Command command = BenchCommands.CreateBenchCommand(ForwardingBenchmarkInput.Raw);
        ParseResult result = command.Parse("viiper --count 10");

        Assert.HasCount(0, result.Errors);
    }
}
