using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Cli.Tools;
using Inputs;
using Inputs.Sdl;
using Outputs;

internal static class XpadCommands
{
    internal static string DisplayButtons(GamepadButtons buttons)
    {
        return buttons == GamepadButtons.None
            ? "none"
            : buttons.ToString();
    }

    // MARK: Command Helpers
    // ========================================================================

    internal static Command CreateProbeCommand()
    {
        Command command = new("probe", "List SDL gamepads.");
        command.SetAction(async (_, _) =>
        {
            await PrintGamepadsAsync(SdlGamepadSource.GetGamepads()).ConfigureAwait(false);
        });

        return command;
    }

    internal static Command CreateInputCommand()
    {
        Command command = new("input", "Read SDL gamepad state changes.");
        Option<int?> deviceIndexOption = CliOptions.CreateDeviceIndexOption(
            "--device-index",
            "Zero-based SDL gamepad index. Default: 0.");
        Option<int?> pollMsOption = CliOptions.CreatePollMsOption(
            "--poll-ms",
            "SDL polling interval in milliseconds. Default: 1.");
        command.Options.Add(deviceIndexOption);
        command.Options.Add(pollMsOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            SdlGamepadOptions options = CliOptions.CreateSdlGamepadOptions(
                parseResult,
                deviceIndexOption,
                pollMsOption);

            await RunInputAsync(options, cancellationToken).ConfigureAwait(false);
        });

        return command;
    }

    internal static Command CreatePressCommand(IServiceProvider? services = null)
    {
        Command command = new("press", "Send a short Xbox 360 test report through VIIPER.");
        Option<int?> durationMsOption = CliOptions.CreateDurationMsOption(250);
        command.Options.Add(durationMsOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            int durationMs = parseResult.GetValue(durationMsOption) ?? 250;
            _ = await ViiperConnection.ExecuteXbox360Async(
                async (output, ct) =>
                {
                    await ViiperConnection.PrintConnectionAsync(output).ConfigureAwait(false);
                    await XpadTestSender
                        .SendButtonPressAsync(output, Xbox360Buttons.A, TimeSpan.FromMilliseconds(durationMs), ct)
                        .ConfigureAwait(false);
                    await Console.Out.WriteLineAsync("xpad press: sent A press.").ConfigureAwait(false);
                    return 0;
                },
                cancellationToken,
                services).ConfigureAwait(false);
        });

        return command;
    }

    // MARK: Helpers
    // ========================================================================

    private static async Task RunInputAsync(
        SdlGamepadOptions options,
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
            using SdlGamepadSource input = await SdlGamepadSource
                .ConnectAsync(options, runCancellation.Token)
                .ConfigureAwait(false);
            XpadInputPrinter printer = new();

            await Console.Out.WriteLineAsync(
                $"xpad input: index={options.DeviceIndex} instance={input.InstanceId} name=\"{input.DeviceName}\". Ctrl+C to stop.")
                .ConfigureAwait(false);
            input.Run(printer.HandleInput, runCancellation.Token);
        }
        catch (OperationCanceledException) when (runCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            Console.CancelKeyPress -= OnCancel;
        }
    }

    private static async Task PrintGamepadsAsync(IReadOnlyList<SdlGamepadInfo> gamepads)
    {
        if (gamepads.Count == 0)
        {
            await Console.Out.WriteLineAsync("no SDL gamepads found").ConfigureAwait(false);
            return;
        }

        await Console.Out.WriteLineAsync("index  instance  name").ConfigureAwait(false);
        foreach (SdlGamepadInfo gamepad in gamepads)
        {
            await Console.Out.WriteLineAsync(
                $"{gamepad.Index,5}  {gamepad.InstanceId,8}  {gamepad.Name}")
                .ConfigureAwait(false);
        }
    }

    private sealed class XpadInputPrinter
    {
        private static readonly long MinPrintIntervalTicks = Stopwatch.Frequency / 10;
        private long lastPrintTimestamp;

        public void HandleInput(in GamepadInput input)
        {
            long timestamp = Stopwatch.GetTimestamp();
            if (lastPrintTimestamp != 0 && timestamp - lastPrintTimestamp < MinPrintIntervalTicks)
            {
                return;
            }

            lastPrintTimestamp = timestamp;

            GamepadState state = input.State;
            Console.WriteLine(
                $"device=\"{input.DeviceName}\" " +
                $"buttons={DisplayButtons(state.Buttons)} " +
                $"lx={state.LeftX} ly={state.LeftY} rx={state.RightX} ry={state.RightY} " +
                $"lt={state.LeftTrigger} rt={state.RightTrigger}");
        }
    }
}
