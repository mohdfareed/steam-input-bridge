using System;
using System.CommandLine;
using System.Runtime.Versioning;
using Cli.Tools.Benchmarks;
using Hosting;

[SupportedOSPlatform("windows")]
internal static class TestCommands
{
    // MARK: Commands
    // ========================================================================

    [SupportedOSPlatform("windows")]
    internal static Command CreateTestCommand(IServiceProvider? services = null)
    {
        Command command = new("test", "Diagnostics, probes, synthetic input, and benchmarks.");
        command.Subcommands.Add(CreateHostCommand());
        command.Subcommands.Add(CreateMouseCommand(services));
        command.Subcommands.Add(CreateXpadCommand(services));
        return command;
    }

    private static Command CreateHostCommand()
    {
        Command command = new("host", "Host diagnostics.");
        command.Subcommands.Add(CreatePipesCommand());
        return command;
    }

    private static Command CreatePipesCommand()
    {
        Command command = new("pipes", "Print host control runtime names.");
        command.SetAction(async (_, _) =>
        {
            await Console.Out.WriteLineAsync(
                $"pipe={ForwardingServer.PipeName} ownership={ForwardingServer.OwnershipName} mouseRoute=mouse")
                .ConfigureAwait(false);
        });

        return command;
    }

    private static Command CreateMouseCommand(IServiceProvider? services)
    {
        Command command = new("mouse", "Mouse diagnostics and test tools.");
        command.Subcommands.Add(InputCommands.CreateMouseInputCommand());
        command.Subcommands.Add(MouseCommands.CreateMouseNullifyCommand(services));
        command.Subcommands.Add(BenchCommands.CreateBenchCommand(ForwardingBenchmarkInput.Raw));
        return command;
    }

    private static Command CreateXpadCommand(IServiceProvider? services)
    {
        Command command = new("xpad", "Gamepad diagnostics and test tools.");
        command.Subcommands.Add(XpadCommands.CreateProbeCommand());
        command.Subcommands.Add(XpadCommands.CreateInputCommand());
        command.Subcommands.Add(XpadCommands.CreatePressCommand(services));
        command.Subcommands.Add(BenchCommands.CreateBenchCommand(ForwardingBenchmarkInput.Sdl));
        return command;
    }
}
