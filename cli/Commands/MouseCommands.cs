using System;
using System.CommandLine;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Cli.Tools;
using Hosting;
using Outputs;

internal static class MouseCommands
{
    // MARK: Commands
    // ========================================================================

    [SupportedOSPlatform("windows")]
    internal static Command CreateMouseCommand(IServiceProvider? services = null)
    {
        Command command = new("mouse", "Forward Raw Input mouse reports.");
        command.Subcommands.Add(CreateRunCommand(services));
        command.Subcommands.Add(CreateForwardCommand(services));
        command.Subcommands.Add(CreateNullifyCommand(services));
        return command;
    }

    [SupportedOSPlatform("windows")]
    private static Command CreateRunCommand(IServiceProvider? services)
    {
        return CreateBridgeCommand(
            "run",
            "Start forwarding mouse input.",
            MouseForwarding.RunRawInputToAsync,
            services);
    }

    [SupportedOSPlatform("windows")]
    private static Command CreateForwardCommand(IServiceProvider? services)
    {
        return CreateBridgeCommand(
            "forward",
            "Forward Raw Input mouse reports to the output mouse.",
            MouseForwarding.RunRawInputToAsync,
            services);
    }

    [SupportedOSPlatform("windows")]
    private static Command CreateNullifyCommand(IServiceProvider? services)
    {
        return CreateBridgeCommand(
            "nullify",
            "Send opposite Steam mouse movement to the output mouse.",
            MouseNullifier.RunRawInputToAsync,
            services);
    }

    [SupportedOSPlatform("windows")]
    private static Command CreateBridgeCommand(
        string name,
        string description,
        Func<IMouseOutput, CancellationToken, Task> runAsync,
        IServiceProvider? services)
    {
        Command command = new(name, description);
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            _ = parseResult;

            _ = await ViiperConnection.ExecuteMouseAsync(
                async (mouse, ct) =>
                {
                    await ViiperConnection.PrintConnectionAsync(mouse).ConfigureAwait(false);
                    await Console.Out.WriteLineAsync($"mouse {name}: running. Ctrl+C to stop.").ConfigureAwait(false);
                    await runAsync(mouse, ct).ConfigureAwait(false);
                    return 0;
                },
                cancellationToken,
                services).ConfigureAwait(false);
        });

        return command;
    }
}
