using System;
using System.CommandLine;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Hosting;

internal static class ClientCommands
{
    // MARK: Commands
    // ========================================================================

    internal static Command CreateClientCommand()
    {
        Command command = new("client", "Control a running forwarding host.");
        command.Subcommands.Add(CreateRunCommand());
        return command;
    }

    private static Command CreateRunCommand()
    {
        Command command = new("run", "Enable forwarding for a route until cancelled.");
        Option<ForwardingRouteKind> routeOption = CliOptions.CreateRequiredRouteOption();
        command.Options.Add(routeOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            ForwardingRouteKind route = parseResult.GetValue(routeOption);
            ForwardingEnableLease? lease = await TryEnableAsync(route, cancellationToken).ConfigureAwait(false);
            if (lease is null)
            {
                return;
            }

            await using (lease.ConfigureAwait(false))
            {
                try
                {
                    await Console.Out.WriteLineAsync(
                        $"route={ForwardingServer.GetRouteId(route)} enabled=true. Ctrl+C to release.")
                        .ConfigureAwait(false);
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                }
            }
        });

        return command;
    }

    // MARK: Helpers
    // ========================================================================

    internal static async Task<ForwardingEnableLease?> TryEnableAsync(
        ForwardingRouteKind route,
        CancellationToken cancellationToken)
    {
        ForwardingClient client = new();
        try
        {
            return await client.EnableAsync(route, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            await Console.Error.WriteLineAsync(
                $"client route={ForwardingServer.GetRouteId(route)}: host not running")
                .ConfigureAwait(false);
            Environment.ExitCode = 1;
            return null;
        }
        catch (IOException exception)
        {
            await Console.Error.WriteLineAsync($"client: unavailable ({exception.Message})").ConfigureAwait(false);
            Environment.ExitCode = 1;
            return null;
        }
    }
}
