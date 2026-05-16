using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Threading;
using System.Threading.Tasks;
using Cli.Tools;
using Hosting;
using Inputs;
using Inputs.Sdl;
using Outputs;

internal static class XpadCommands
{
    // MARK: Commands
    // ========================================================================

    internal static Command CreateXpadCommand()
    {
        Command command = new("xpad", "Forward SDL gamepad input to VIIPER Xbox 360 output.");
        command.Subcommands.Add(CreateProbeCommand());
        command.Subcommands.Add(CreateInputCommand());
        command.Subcommands.Add(CreateTestCommand());
        command.Subcommands.Add(CreateRunCommand("run", "Start forwarding xpad input."));
        command.Subcommands.Add(CreateRunCommand("forward", "Forward SDL gamepad input to VIIPER Xbox 360 output."));
        return command;
    }

    internal static string DisplayButtons(GamepadButtons buttons)
    {
        return buttons == GamepadButtons.None
            ? "none"
            : buttons.ToString();
    }

    // MARK: Command Helpers
    // ========================================================================

    private static Command CreateProbeCommand()
    {
        Command command = new("probe", "List SDL gamepads.");
        command.SetAction(async (_, _) =>
        {
            await PrintGamepadsAsync(SdlGamepadSource.GetGamepads()).ConfigureAwait(false);
        });

        return command;
    }

    private static Command CreateInputCommand()
    {
        Command command = new("input", "Read SDL gamepad state changes.");
        Option<int?> deviceIndexOption = CliOptions.CreateDeviceIndexOption(
            "Zero-based SDL gamepad index. Default: 0.");
        Option<int?> pollMsOption = CliOptions.CreatePollMsOption(
            "SDL polling interval in milliseconds. Default: 1.");
        command.Options.Add(deviceIndexOption);
        command.Options.Add(pollMsOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            SdlGamepadOptions options = CliOptions.CreateSdlGamepadOptions(
                parseResult,
                deviceIndexOption,
                pollMsOption);
            using SdlGamepadSource input = await SdlGamepadSource
                .ConnectAsync(options, cancellationToken)
                .ConfigureAwait(false);

            await Console.Out.WriteLineAsync($"xpad input: {input.DeviceName}. Ctrl+C to stop.").ConfigureAwait(false);
            input.Run(HandleInput, cancellationToken);
        });

        return command;
    }

    private static Command CreateTestCommand()
    {
        Command command = new("test", "Send a short Xbox 360 test report through VIIPER.");
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
                    await Console.Out.WriteLineAsync("xpad test: sent A press.").ConfigureAwait(false);
                    return 0;
                },
                cancellationToken).ConfigureAwait(false);
        });

        return command;
    }

    private static Command CreateRunCommand(string name, string description)
    {
        Command command = new(name, description);
        command.SetAction(async (_, cancellationToken) =>
        {
            ForwardingEnableLease? lease = await HostCommands
                .TryEnableHostAsync(ForwardingRouteKind.Xpad, cancellationToken)
                .ConfigureAwait(false);
            if (lease is null)
            {
                return;
            }

            await using (lease.ConfigureAwait(false))
            {
                try
                {
                    await Console.Out.WriteLineAsync(
                        $"xpad {name}: enabled through host. Ctrl+C to release.")
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

    private static void HandleInput(in GamepadInput input)
    {
        GamepadState state = input.State;
        Console.WriteLine(
            $"device=\"{input.DeviceName}\" " +
            $"buttons={DisplayButtons(state.Buttons)} " +
            $"lx={state.LeftX} ly={state.LeftY} rx={state.RightX} ry={state.RightY} " +
            $"lt={state.LeftTrigger} rt={state.RightTrigger}");
    }

}
