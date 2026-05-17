using System;
using System.CommandLine;
using System.Runtime.Versioning;
using System.Threading;
using Inputs;
using Inputs.RawInput;

internal static class InputCommands
{
    // MARK: Commands
    // ========================================================================

    [SupportedOSPlatform("windows")]
    internal static Command CreateMouseInputCommand()
    {
        Command command = new("input", "Read Windows Raw Input mouse reports.");
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            _ = parseResult;

            using RawInputMouseSource input = await RawInputMouseSource
                .ConnectAsync(cancellationToken)
                .ConfigureAwait(false);

            await Console.Out.WriteLineAsync("input: running. Ctrl+C to stop.").ConfigureAwait(false);
            RunInput(input, cancellationToken);
        });

        return command;
    }

    // MARK: Helpers
    // ========================================================================

    private static void RunInput<TInput>(TInput input, CancellationToken cancellationToken)
        where TInput : IMouseInputSource
    {
        MouseButtons previousButtons = MouseButtons.None;

        input.Run(HandleInput, cancellationToken);

        void HandleInput(in MouseInput source)
        {
            MouseButtons pressed = source.Report.Buttons & ~previousButtons;
            MouseButtons released = previousButtons & ~source.Report.Buttons;
            previousButtons = source.Report.Buttons;

            Console.WriteLine(
                $"device=\"{DisplayDeviceName(source.DeviceName)}\" " +
                $"dx={source.Report.DeltaX} dy={source.Report.DeltaY} wheel={source.Report.WheelDelta} " +
                $"buttons={DisplayButtons(source.Report.Buttons)} " +
                $"pressed={DisplayButtons(pressed)} released={DisplayButtons(released)}");
        }
    }

    internal static string DisplayButtons(MouseButtons buttons)
    {
        return buttons == MouseButtons.None
            ? "none"
            : buttons.ToString();
    }

    private static string DisplayDeviceName(string deviceName)
    {
        return string.IsNullOrWhiteSpace(deviceName) ? "(unknown)" : deviceName;
    }
}
