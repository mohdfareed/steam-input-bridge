using System;
using System.CommandLine;
using System.Runtime.Versioning;
using System.Threading;
using PhysicalMouse;
using VirtualMouse;
using VirtualMouse.RawInput;

internal static class CliInputCommands
{
    // MARK: Commands
    // ========================================================================

    [SupportedOSPlatform("windows")]
    internal static Command CreateInputCommand()
    {
        Command command = new("input", "Read mouse input sources.");
        command.Subcommands.Add(CreateRawCommand());
        return command;
    }

    [SupportedOSPlatform("windows")]
    private static Command CreateRawCommand()
    {
        Command command = new("raw", "Read Windows Raw Input mouse reports.");
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            _ = parseResult;

            using RawInputVirtualMouse input = await RawInputVirtualMouse
                .ConnectAsync(cancellationToken)
                .ConfigureAwait(false);

            await Console.Out.WriteLineAsync("input raw: running. Ctrl+C to stop.").ConfigureAwait(false);
            RunInput(input, cancellationToken);
        });

        return command;
    }

    // MARK: Helpers
    // ========================================================================

    private static void RunInput<TInput>(TInput input, CancellationToken cancellationToken)
        where TInput : IVirtualMouse
    {
        MouseButtons previousButtons = MouseButtons.None;

        input.Run(HandleInput, cancellationToken);

        void HandleInput(in VirtualMouseInput source)
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
