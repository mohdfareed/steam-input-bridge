using System;
using System.CommandLine;
using System.Diagnostics;
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
        Option<int> intervalMsOption = new("--interval-ms")
        {
            Description = "Console update interval. Default: 250.",
            DefaultValueFactory = _ => 250,
        };
        command.Options.Add(intervalMsOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            using RawInputMouseSource input = await RawInputMouseSource
                .ConnectAsync(cancellationToken)
                .ConfigureAwait(false);

            await Console.Out.WriteLineAsync("input: running. Ctrl+C to stop.").ConfigureAwait(false);
            RunInput(input, TimeSpan.FromMilliseconds(parseResult.GetValue(intervalMsOption)), cancellationToken);
        });

        return command;
    }

    // MARK: Privates
    // ========================================================================

    private static void RunInput<TInput>(TInput input, TimeSpan outputInterval, CancellationToken cancellationToken)
        where TInput : IMouseInputSource
    {
        MouseInputBatch batch = new(outputInterval);
        MouseButtons previousButtons = MouseButtons.None;

        input.Run(HandleInput, cancellationToken);

        void HandleInput(in MouseInput source)
        {
            MouseButtons pressed = source.Report.Buttons & ~previousButtons;
            MouseButtons released = previousButtons & ~source.Report.Buttons;
            previousButtons = source.Report.Buttons;

            batch.Add(source, pressed, released);
            if (batch.ShouldFlush())
            {
                batch.Flush();
            }
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

    private sealed class MouseInputBatch(TimeSpan outputInterval)
    {
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private readonly TimeSpan _outputInterval = outputInterval > TimeSpan.Zero
            ? outputInterval
            : TimeSpan.FromMilliseconds(250);

        private string _deviceName = string.Empty;
        private int _deltaX;
        private int _deltaY;
        private int _wheelDelta;
        private uint _reports;
        private MouseButtons _buttons;
        private MouseButtons _pressed;
        private MouseButtons _released;
        private TimeSpan _lastFlush;

        public void Add(in MouseInput input, MouseButtons pressed, MouseButtons released)
        {
            _deviceName = input.DeviceName;
            _deltaX += input.Report.DeltaX;
            _deltaY += input.Report.DeltaY;
            _wheelDelta += input.Report.WheelDelta;
            _buttons = input.Report.Buttons;
            _pressed |= pressed;
            _released |= released;
            _reports++;
        }

        public bool ShouldFlush()
        {
            return _reports > 0 &&
                _stopwatch.Elapsed - _lastFlush >= _outputInterval;
        }

        public void Flush()
        {
            Console.WriteLine(
                $"reports={_reports} device=\"{DisplayDeviceName(_deviceName)}\" " +
                $"dx={_deltaX} dy={_deltaY} wheel={_wheelDelta} " +
                $"buttons={DisplayButtons(_buttons)} " +
                $"pressed={DisplayButtons(_pressed)} released={DisplayButtons(_released)}");

            _deltaX = 0;
            _deltaY = 0;
            _wheelDelta = 0;
            _reports = 0;
            _pressed = MouseButtons.None;
            _released = MouseButtons.None;
            _lastFlush = _stopwatch.Elapsed;
        }
    }
}
