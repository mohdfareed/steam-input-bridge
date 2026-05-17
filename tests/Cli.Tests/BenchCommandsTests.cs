using System.Linq;
using Cli.Tools.Benchmarks;

namespace Cli.Tests;

/// <summary>Tests for benchmark CLI helpers.</summary>
[TestClass]
public sealed class BenchCommandsTests
{
    /// <summary>Checks benchmark command shape.</summary>
    [TestMethod]
    public void CreateBenchCommandHasInputAndOutputArguments()
    {
        System.CommandLine.Command command = BenchCommands.CreateBenchCommand();

        Assert.HasCount(0, command.Subcommands);
        Assert.HasCount(2, command.Arguments);
        CollectionAssert.Contains(command.Options.Select(option => option.Name).ToArray(), "--count");
    }

    /// <summary>Checks fixed-input benchmark command shape.</summary>
    [TestMethod]
    public void CreateBenchCommandWithFixedInputHasOutputArgumentOnly()
    {
        System.CommandLine.Command command = BenchCommands.CreateBenchCommand(ForwardingBenchmarkInput.Raw);

        Assert.HasCount(1, command.Arguments);
        CollectionAssert.Contains(command.Options.Select(option => option.Name).ToArray(), "--count");
    }
}
