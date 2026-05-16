using System;
using System.CommandLine;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Hosting;
using Microsoft.Extensions.Logging;

internal static class HostCommands
{
    // MARK: Commands
    // ========================================================================

    [SupportedOSPlatform("windows")]
    internal static Command CreateHostCommand()
    {
        Command command = new("host", "Control the local forwarding host.");
        command.Subcommands.Add(CreateRunCommand());
        command.Subcommands.Add(CreateEnableCommand());
        command.Subcommands.Add(CreateStatusCommand());
        return command;
    }

    [SupportedOSPlatform("windows")]
    private static Command CreateRunCommand()
    {
        Command command = new("run", "Run the local forwarding host.");
        Option<ForwardingRouteKind?> routeOption = CliOptions.CreateRouteOption();
        Option<int?> deviceIndexOption = CliOptions.CreateDeviceIndexOption(
            "Zero-based SDL gamepad index for xpad hosts. Default: 0.");
        Option<int?> pollMsOption = CliOptions.CreatePollMsOption(
            "SDL polling interval in milliseconds for xpad hosts. Default: 1.");
        command.Options.Add(routeOption);
        command.Options.Add(deviceIndexOption);
        command.Options.Add(pollMsOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            ILogger logger = new ConsoleLogger("host");
            ForwardingRouteKind route = parseResult.GetValue(routeOption) ?? ForwardingRouteKind.Mouse;
            ForwardingServerOptions options = new()
            {
                Route = route,
                SdlGamepad = CliOptions.CreateSdlGamepadOptions(parseResult, deviceIndexOption, pollMsOption),
                Viiper = ViiperConnection.CreateViiperOptions(logger),
                Logger = logger,
            };

            await RunHostAsync(options, cancellationToken).ConfigureAwait(false);
        });

        return command;
    }

    private static Command CreateEnableCommand()
    {
        Command command = new("enable", "Enable host forwarding until cancelled.");
        Option<ForwardingRouteKind?> routeOption = CliOptions.CreateRouteOption();
        command.Options.Add(routeOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            ForwardingRouteKind route = parseResult.GetValue(routeOption) ?? ForwardingRouteKind.Mouse;
            ForwardingEnableLease? lease = await TryEnableHostAsync(route, cancellationToken).ConfigureAwait(false);
            if (lease is null)
            {
                return;
            }

            await using (lease.ConfigureAwait(false))
            {
                try
                {
                    await Console.Out.WriteLineAsync(
                        $"route={DisplayRoute(route)} enabled=true. Ctrl+C to release.")
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

    private static Command CreateStatusCommand()
    {
        Command command = new("status", "Print host status.");
        Option<ForwardingRouteKind?> routeOption = CliOptions.CreateRouteOption();
        command.Options.Add(routeOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            ForwardingRouteKind route = parseResult.GetValue(routeOption) ?? ForwardingRouteKind.Mouse;
            ForwardingStatus? maybeStatus = await TryGetStatusAsync(route, cancellationToken).ConfigureAwait(false);
            if (!maybeStatus.HasValue)
            {
                return;
            }

            ForwardingStatus status = maybeStatus.Value;
            string enabled = status.IsEnabled ? "true" : "false";
            string connected = status.IsConnected ? "true" : "false";
            await Console.Out.WriteLineAsync(
                $"route={status.RouteId} enabled={enabled} connected={connected} enabledClients={status.EnabledClientCount}")
                .ConfigureAwait(false);
        });

        return command;
    }

    // MARK: Host
    // ========================================================================

    [SupportedOSPlatform("windows")]
    private static async Task RunHostAsync(
        ForwardingServerOptions options,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        void OnCancel(object? sender, ConsoleCancelEventArgs eventArgs)
        {
            eventArgs.Cancel = true;
            runCancellation.Cancel();
        }

        Console.CancelKeyPress += OnCancel;

        try
        {
            await Console.Out.WriteLineAsync(
                $"host: starting route={DisplayRoute(options.Route)}. Ctrl+C to stop.")
                .ConfigureAwait(false);
            ForwardingServer server = new(options);
            await using (server.ConfigureAwait(false))
            {
                await server.RunAsync(runCancellation.Token).ConfigureAwait(false);
            }
        }
        finally
        {
            Console.CancelKeyPress -= OnCancel;
        }
    }

    internal static async Task<ForwardingEnableLease?> TryEnableHostAsync(
        ForwardingRouteKind route,
        CancellationToken cancellationToken)
    {
        ForwardingClient client = CreateControlClient(route);
        try
        {
            return await client.EnableAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            await Console.Error.WriteLineAsync($"host route={DisplayRoute(route)}: not running").ConfigureAwait(false);
            Environment.ExitCode = 1;
            return null;
        }
        catch (IOException exception)
        {
            await Console.Error.WriteLineAsync($"host: unavailable ({exception.Message})").ConfigureAwait(false);
            Environment.ExitCode = 1;
            return null;
        }
    }

    private static async Task<ForwardingStatus?> TryGetStatusAsync(
        ForwardingRouteKind route,
        CancellationToken cancellationToken)
    {
        ForwardingClient client = CreateControlClient(route);
        try
        {
            return await client.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            await Console.Out.WriteLineAsync($"route={DisplayRoute(route)} running=false").ConfigureAwait(false);
            return null;
        }
        catch (IOException exception)
        {
            await Console.Error.WriteLineAsync($"host: unavailable ({exception.Message})").ConfigureAwait(false);
            Environment.ExitCode = 1;
            return null;
        }
    }

    private static ForwardingClient CreateControlClient(ForwardingRouteKind route)
    {
        return new ForwardingClient(route);
    }

    private static string DisplayRoute(ForwardingRouteKind route)
    {
        return route switch
        {
            ForwardingRouteKind.Mouse => "mouse",
            ForwardingRouteKind.Xpad => "xpad",
            _ => throw new ArgumentOutOfRangeException(nameof(route)),
        };
    }
}
