using System;
using System.CommandLine;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Cli.Tools;
using Outputs;

internal static class MouseCommands
{
    [SupportedOSPlatform("windows")]
    internal static Command CreateMouseNullifyCommand(IServiceProvider? services = null)
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
