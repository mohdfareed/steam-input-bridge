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
    private static readonly TimeSpan ProbeRetryInterval = TimeSpan.FromMilliseconds(250);

    internal static string DisplayButtons(GamepadButtons buttons)
    {
        return buttons == GamepadButtons.None
            ? "none"
            : buttons.ToString();
    }

    internal static string DisplayMotion(GamepadMotion motion)
    {
        return $"gyro={DisplayVector(motion.HasGyro, motion.GyroX, motion.GyroY, motion.GyroZ)} " +
            $"accel={DisplayVector(motion.HasAccelerometer, motion.AccelX, motion.AccelY, motion.AccelZ)}";
    }

    internal static Command CreateProbeCommand()
    {
        Command command = new("probe", "List SDL gamepads.");
        Option<int?> waitMsOption = CliOptions.CreateWaitMsOption(0);
        Option<bool> pauseOption = CliOptions.CreatePauseOption();
        command.Options.Add(waitMsOption);
        command.Options.Add(pauseOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            try
            {
                IReadOnlyList<SdlControllerInfo> controllers = await WaitForControllersAsync(
                    TimeSpan.FromMilliseconds(parseResult.GetValue(waitMsOption) ?? 0),
                    cancellationToken).ConfigureAwait(false);
                await PrintControllersAsync(controllers).ConfigureAwait(false);
            }
            finally
            {
                await PauseIfRequestedAsync(parseResult.GetValue(pauseOption), cancellationToken).ConfigureAwait(false);
            }
        });

        return command;
    }

    internal static Command CreateInputCommand()
    {
        Command command = new("input", "Read SDL gamepad state changes.");
        Option<string?> deviceOption = CliOptions.CreateGamepadOption(
            "--device",
            "SDL controller id or unique display name.");
        Option<bool> allOption = new("--all")
        {
            Description = "Read all visible SDL gamepads.",
        };
        Option<bool> noMotionOption = new("--no-motion")
        {
            Description = "Do not emit motion data.",
        };
        Option<int> intervalMsOption = new("--interval-ms")
        {
            Description = "Console update interval. Default: 250.",
            DefaultValueFactory = _ => 250,
        };
        Option<int?> waitMsOption = CliOptions.CreateWaitMsOption(0);
        Option<bool> pauseOption = CliOptions.CreatePauseOption();
        command.Options.Add(deviceOption);
        command.Options.Add(allOption);
        command.Options.Add(noMotionOption);
        command.Options.Add(intervalMsOption);
        command.Options.Add(waitMsOption);
        command.Options.Add(pauseOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            using CancellationTokenSource runCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            void Cancel(object? _, ConsoleCancelEventArgs eventArgs)
            {
                eventArgs.Cancel = true;
                runCancellation.Cancel();
            }

            ConsoleCancelEventHandler cancel = Cancel;
            Console.CancelKeyPress += cancel;

            try
            {
                IReadOnlyList<SdlControllerInfo> controllers = await WaitForControllersAsync(
                    TimeSpan.FromMilliseconds(parseResult.GetValue(waitMsOption) ?? 0),
                    runCancellation.Token).ConfigureAwait(false);
                bool readAll = parseResult.GetValue(allOption);
                SdlGamepadSource[] sources = readAll
                    ? await ConnectAllAsync(controllers, runCancellation.Token).ConfigureAwait(false)
                    : [await ConnectOneAsync(
                        controllers,
                        parseResult.GetValue(deviceOption),
                        runCancellation.Token).ConfigureAwait(false)];

                try
                {
                    foreach (SdlGamepadSource source in sources)
                    {
                        source.MotionEnabled = !parseResult.GetValue(noMotionOption);
                    }

                    await Console.Out.WriteLineAsync("xpad input: Ctrl+C to stop.").ConfigureAwait(false);
                    XpadInputBatch batch = new(TimeSpan.FromMilliseconds(parseResult.GetValue(intervalMsOption)));
                    SdlControllerInputLoop.Run(sources, batch.Add, runCancellation.Token);
                }
                catch (SdlGamepadDisconnectedException exception)
                {
                    await Console.Error.WriteLineAsync($"xpad input: {exception.Message}").ConfigureAwait(false);
                }
                finally
                {
                    foreach (SdlGamepadSource source in sources)
                    {
                        await source.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                Console.CancelKeyPress -= cancel;
                await PauseIfRequestedAsync(parseResult.GetValue(pauseOption), CancellationToken.None)
                    .ConfigureAwait(false);
            }
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

    private static async Task<SdlGamepadSource[]> ConnectAllAsync(
        IReadOnlyList<SdlControllerInfo> controllers,
        CancellationToken cancellationToken)
    {
        List<SdlGamepadSource> sources = new(controllers.Count);
        try
        {
            foreach (SdlControllerInfo controller in controllers)
            {
                sources.Add(await SdlGamepadSource.ConnectAsync(controller, cancellationToken).ConfigureAwait(false));
            }

            return [.. sources];
        }
        catch
        {
            foreach (SdlGamepadSource source in sources)
            {
                await source.DisposeAsync().ConfigureAwait(false);
            }

            throw;
        }
    }

    private static Task<SdlGamepadSource> ConnectOneAsync(
        IReadOnlyList<SdlControllerInfo> controllers,
        string? selector,
        CancellationToken cancellationToken)
    {
        return SdlGamepadSource.ConnectAsync(ResolveController(controllers, selector), cancellationToken);
    }

    private static SdlControllerInfo ResolveController(
        IReadOnlyList<SdlControllerInfo> controllers,
        string? selector)
    {
        if (controllers.Count == 0)
        {
            throw new InvalidOperationException("No SDL gamepads found.");
        }

        if (string.IsNullOrWhiteSpace(selector))
        {
            return controllers.Count == 1
                ? controllers[0]
                : throw new InvalidOperationException("Multiple SDL gamepads found; pass --device.");
        }

        SdlControllerInfo? match = null;
        foreach (SdlControllerInfo controller in controllers)
        {
            if (!Matches(controller, selector))
            {
                continue;
            }

            if (match is not null)
            {
                throw new InvalidOperationException($"SDL controller selector \"{selector}\" matches multiple devices.");
            }

            match = controller;
        }

        return match ?? throw new InvalidOperationException($"SDL controller \"{selector}\" was not found.");
    }

    private static bool Matches(SdlControllerInfo controller, string selector)
    {
        return string.Equals(controller.Id.Value, selector, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(controller.Name, selector, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(DisplayName(controller), selector, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<IReadOnlyList<SdlControllerInfo>> WaitForControllersAsync(
        TimeSpan wait,
        CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<SdlControllerInfo> controllers = SdlControllerCatalog.GetControllers();
            if (controllers.Count > 0 || stopwatch.Elapsed >= wait)
            {
                return controllers;
            }

            await Task.Delay(ProbeRetryInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task PrintControllersAsync(IReadOnlyList<SdlControllerInfo> controllers)
    {
        if (controllers.Count == 0)
        {
            await Console.Out.WriteLineAsync("no SDL gamepads found").ConfigureAwait(false);
            return;
        }

        await Console.Out.WriteLineAsync("id                                          source    vid   pid   gyro  accel  name")
            .ConfigureAwait(false);
        foreach (SdlControllerInfo controller in controllers)
        {
            await Console.Out.WriteLineAsync(
                $"{controller.Id.Value,-43} {DisplaySource(controller),-8} " +
                $"{controller.VendorId:x4}  {controller.ProductId:x4}  " +
                $"{FormatBool(controller.HasGyro),5}  {FormatBool(controller.HasAccelerometer),5}  " +
                $"{controller.Name}")
                .ConfigureAwait(false);
        }
    }

    private static string DisplayName(SdlControllerInfo controller)
    {
        return controller.Source == SdlControllerSource.Steam
            ? $"{controller.Name} (steam)"
            : controller.Name;
    }

    private static string DisplaySource(SdlControllerInfo controller)
    {
        return controller.Source == SdlControllerSource.Steam ? "steam" : "physical";
    }

    private static string DisplayVector(bool hasValue, float x, float y, float z)
    {
        return hasValue
            ? $"{x:0.###},{y:0.###},{z:0.###}"
            : "none";
    }

    private static string FormatBool(bool value)
    {
        return value ? "true" : "false";
    }

    private static async Task PauseIfRequestedAsync(bool pause, CancellationToken cancellationToken)
    {
        if (!pause)
        {
            return;
        }

        await Console.Out.WriteLineAsync("press Enter to exit").ConfigureAwait(false);
        _ = await Console.In.ReadLineAsync(cancellationToken).ConfigureAwait(false);
    }

    private sealed class XpadInputBatch(TimeSpan outputInterval)
    {
        private readonly Dictionary<SdlGamepadSource, Entry> _entries = [];
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private readonly TimeSpan _outputInterval = outputInterval > TimeSpan.Zero
            ? outputInterval
            : TimeSpan.FromMilliseconds(250);

        private TimeSpan _lastFlush;

        public void Add(SdlGamepadSource source, GamepadInput input)
        {
            _ = _entries.TryGetValue(source, out Entry entry);
            entry.State = input.State;
            entry.Reports++;
            _entries[source] = entry;

            if (_stopwatch.Elapsed - _lastFlush >= _outputInterval)
            {
                Flush();
            }
        }

        private void Flush()
        {
            if (_entries.Count == 0)
            {
                return;
            }

            foreach (KeyValuePair<SdlGamepadSource, Entry> item in _entries)
            {
                if (item.Value.Reports == 0)
                {
                    continue;
                }

                GamepadState state = item.Value.State;
                Console.Out.WriteLine(
                    $"reports={item.Value.Reports} {DisplayName(item.Key.Controller)} " +
                    $"buttons={DisplayButtons(state.Buttons)} " +
                    $"lx={state.LeftX} ly={state.LeftY} rx={state.RightX} ry={state.RightY} " +
                    $"lt={state.LeftTrigger} rt={state.RightTrigger} " +
                    DisplayMotion(state.Motion));
            }

            SdlGamepadSource[] sources = [.. _entries.Keys];
            foreach (SdlGamepadSource source in sources)
            {
                Entry entry = _entries[source];
                entry.Reports = 0;
                _entries[source] = entry;
            }

            _lastFlush = _stopwatch.Elapsed;
        }

        private struct Entry
        {
            public GamepadState State;
            public uint Reports;
        }
    }
}
