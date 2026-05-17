using System;
using System.CommandLine;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

internal static class HostCommands
{
    // MARK: Commands
    // ========================================================================

    [SupportedOSPlatform("windows")]
    internal static Command CreateHostCommand(IServiceProvider? services = null)
    {
        Command command = new("host", "Control the local forwarding host.");
        command.Subcommands.Add(CreateRunCommand(services));
        command.Subcommands.Add(CreateStatusCommand());
        command.Subcommands.Add(CreateStopCommand());
        return command;
    }

    [SupportedOSPlatform("windows")]
    private static Command CreateRunCommand(IServiceProvider? services)
    {
        Command command = new("run", "Run the local forwarding host.");

        command.SetAction(async (_, cancellationToken) =>
        {
            ILogger logger = CreateLogger(services);
            ForwardingServerOptions options = new()
            {
                Viiper = ViiperConnection.CreateViiperOptions(services, logger),
                Logger = logger,
            };

            await RunHostAsync(options, cancellationToken).ConfigureAwait(false);
        });

        return command;
    }

    private static Command CreateStatusCommand()
    {
        Command command = new("status", "Print host status.");

        command.SetAction(async (_, cancellationToken) =>
        {
            ForwardingHostStatus? maybeStatus = await TryGetStatusAsync(cancellationToken).ConfigureAwait(false);
            if (!maybeStatus.HasValue)
            {
                return;
            }

            ForwardingHostStatus status = maybeStatus.Value;
            await Console.Out.WriteLineAsync(
                $"host running=true emulationEnabled={FormatBool(status.EmulationEnabled)} " +
                $"physicalMotionEnabled={FormatBool(status.PhysicalMotionEnabled)}")
                .ConfigureAwait(false);
            await PrintRouteStatusAsync(status.Mouse).ConfigureAwait(false);
            await PrintGamepadStatusesAsync(status.Gamepads).ConfigureAwait(false);
        });

        return command;
    }

    private static Command CreateStopCommand()
    {
        Command command = new("stop", "Request a running host to stop.");
        command.SetAction(async (_, cancellationToken) =>
        {
            ForwardingClient client = new();

            try
            {
                await client.StopAsync(cancellationToken).ConfigureAwait(false);
                await Console.Out.WriteLineAsync("host stopRequested=true").ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                await Console.Error.WriteLineAsync("host: not running").ConfigureAwait(false);
                Environment.ExitCode = 1;
            }
            catch (IOException exception)
            {
                await Console.Error.WriteLineAsync($"host: unavailable ({exception.Message})").ConfigureAwait(false);
                Environment.ExitCode = 1;
            }
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
                "host: starting. Ctrl+C to stop.")
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

    private static async Task<ForwardingHostStatus?> TryGetStatusAsync(
        CancellationToken cancellationToken)
    {
        ForwardingClient client = new();
        try
        {
            return await client.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            await Console.Out.WriteLineAsync("host running=false").ConfigureAwait(false);
            return null;
        }
        catch (IOException exception)
        {
            await Console.Error.WriteLineAsync($"host: unavailable ({exception.Message})").ConfigureAwait(false);
            Environment.ExitCode = 1;
            return null;
        }
    }

    private static ILogger CreateLogger(IServiceProvider? services)
    {
        ILoggerFactory factory = services?.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance;
        return factory.CreateLogger("host");
    }

    private static Task PrintRouteStatusAsync(ForwardingRouteStatus status)
    {
        return Console.Out.WriteLineAsync(
            $"route={status.RouteId} connected={(status.IsConnected ? "true" : "false")} enabledClients={status.EnabledClientCount}");
    }

    private static async Task PrintGamepadStatusesAsync(
        System.Collections.Generic.IReadOnlyList<GamepadControllerSlotStatus> statuses)
    {
        foreach (GamepadControllerSlotStatus status in statuses)
        {
            await Console.Out.WriteLineAsync(
                $"gamepad physical=\"{status.PhysicalControllerName}\" " +
                $"id={status.PhysicalControllerId.Value} " +
                $"clients={status.AttachedClients} " +
                $"inputConnected={FormatBool(status.InputConnected)} " +
                $"outputConnected={FormatBool(status.OutputConnected)} " +
                $"output={FormatOutput(status.OutputBusId, status.OutputDeviceId)}")
                .ConfigureAwait(false);
        }
    }

    private static string FormatBool(bool value)
    {
        return value ? "true" : "false";
    }

    private static string FormatOutput(uint? busId, string? deviceId)
    {
        return busId.HasValue && !string.IsNullOrWhiteSpace(deviceId)
            ? $"{busId.Value}/{deviceId}"
            : "none";
    }

}
