using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Cli.Tools;
using Hosting;
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

    // MARK: Commands
    // ========================================================================

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
                int waitMs = parseResult.GetValue(waitMsOption) ?? 0;
                IReadOnlyList<SdlGamepadInfo> gamepads = await WaitForGamepadsAsync(
                    TimeSpan.FromMilliseconds(waitMs),
                    cancellationToken).ConfigureAwait(false);
                await PrintGamepadsAsync(gamepads).ConfigureAwait(false);
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
        Option<int?> deviceIndexOption = CliOptions.CreateDeviceIndexOption(
            "--device-index",
            "Zero-based SDL gamepad index. Default: 0.");
        Option<int?> motionDeviceIndexOption = CliOptions.CreateDeviceIndexOption(
            "--motion-device-index",
            "Zero-based SDL gamepad index for motion and rumble.");
        Option<bool> noMotionOption = new("--no-motion")
        {
            Description = "Do not emit motion data.",
        };
        Option<bool> allOption = new("--all")
        {
            Description = "Read all visible SDL gamepads.",
        };
        Option<int?> waitMsOption = CliOptions.CreateWaitMsOption(0);
        Option<bool> pauseOption = CliOptions.CreatePauseOption();
        command.Options.Add(deviceIndexOption);
        command.Options.Add(motionDeviceIndexOption);
        command.Options.Add(noMotionOption);
        command.Options.Add(allOption);
        command.Options.Add(waitMsOption);
        command.Options.Add(pauseOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            int waitMs = parseResult.GetValue(waitMsOption) ?? 0;
            bool all = parseResult.GetValue(allOption);
            SdlGamepadOptions options = new()
            {
                DeviceIndex = parseResult.GetValue(deviceIndexOption) ?? 0,
                MotionDeviceIndex = parseResult.GetValue(motionDeviceIndexOption),
            };

            TimeSpan wait = TimeSpan.FromMilliseconds(waitMs);
            bool noMotion = parseResult.GetValue(noMotionOption);
            bool pause = parseResult.GetValue(pauseOption);
            if (all)
            {
                await RunAllInputAsync(options, wait, noMotion, pause, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await RunInputAsync(options, wait, noMotion, pause, cancellationToken).ConfigureAwait(false);
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

    internal static Command CreateMotionSidecarCommand()
    {
        Command command = new("motion-sidecar", "Read physical SDL motion for a Steam-routed gamepad.")
        {
            Hidden = true,
        };
        Option<int?> deviceIndexOption = CliOptions.CreateDeviceIndexOption(
            "--device-index",
            "Zero-based physical SDL gamepad index.");
        Option<string> nameOption = new("--name")
        {
            Required = true,
        };
        Option<int> vendorIdOption = new("--vid")
        {
            Required = true,
        };
        Option<int> productIdOption = new("--pid")
        {
            Required = true,
        };
        Option<int?> waitMsOption = CliOptions.CreateWaitMsOption(0);
        command.Options.Add(deviceIndexOption);
        command.Options.Add(nameOption);
        command.Options.Add(vendorIdOption);
        command.Options.Add(productIdOption);
        command.Options.Add(waitMsOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            SdlGamepadInfo steamGamepad = new(
                Index: -1,
                InstanceId: 0,
                parseResult.GetValue(nameOption) ?? string.Empty,
                SteamHandle: 1,
                checked((ushort)parseResult.GetValue(vendorIdOption)),
                checked((ushort)parseResult.GetValue(productIdOption)),
                Path: null);
            int waitMs = parseResult.GetValue(waitMsOption) ?? 0;
            await RunMotionSidecarAsync(
                steamGamepad,
                parseResult.GetValue(deviceIndexOption),
                TimeSpan.FromMilliseconds(waitMs),
                cancellationToken).ConfigureAwait(false);
        });

        return command;
    }

    // MARK: Input
    // ========================================================================

    private static async Task RunInputAsync(
        SdlGamepadOptions options,
        TimeSpan wait,
        bool noMotion,
        bool pause,
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
            IReadOnlyList<SdlGamepadInfo> gamepads = await WaitForGamepadsAsync(wait, runCancellation.Token)
                .ConfigureAwait(false);
            SdlGamepadInfo primary = SdlGamepadMotionSelector.GetGamepad(gamepads, options.DeviceIndex);
            bool useSidecarMotion = false;
            int? sidecarMotionDeviceIndex = null;
            if (!options.MotionDeviceIndex.HasValue)
            {
                options = SdlGamepadMotionSelector.ResolveOptions(gamepads, options);
            }
            else if (!ContainsGamepad(gamepads, options.MotionDeviceIndex.Value))
            {
                sidecarMotionDeviceIndex = options.MotionDeviceIndex;
                options = options with
                {
                    MotionDeviceIndex = null,
                };
            }

            using SdlGamepadSource input = await ConnectWithWaitAsync(
                options,
                wait,
                runCancellation.Token).ConfigureAwait(false);
            input.MotionEnabled = !noMotion;
            useSidecarMotion = !noMotion &&
                input.IsSteamInput &&
                !input.HasGyro &&
                !input.HasAccelerometer;
            using MotionSidecar? sidecar = useSidecarMotion
                ? StartMotionSidecar(primary, sidecarMotionDeviceIndex, wait, runCancellation.Token)
                : null;
            XpadInputPrinter printer = new(DisplayGamepadName(input.DeviceName, input.IsSteamInput));

            await Console.Out.WriteLineAsync(
                $"xpad input: {DisplayGamepadName(input.DeviceName, input.IsSteamInput)} " +
                $"index={options.DeviceIndex} motionDevice={FormatMotionDevice(input, sidecar)} " +
                $"gyro={FormatBool(input.HasGyro)} accel={FormatBool(input.HasAccelerometer)}. Ctrl+C to stop.")
                .ConfigureAwait(false);
            if (input.MotionDeviceName is not null)
            {
                await Console.Out.WriteLineAsync(
                    $"xpad motion: {input.MotionDeviceName} index={options.MotionDeviceIndex}")
                    .ConfigureAwait(false);
            }
            else if (sidecar is not null)
            {
                await Console.Out.WriteLineAsync("xpad motion: physical sidecar").ConfigureAwait(false);
            }

            input.Run(HandleInput, cancellationToken: runCancellation.Token);

            void HandleInput(in GamepadInput source)
            {
                GamepadInput merged = MergeMotion(source, sidecar?.LatestMotion);
                printer.HandleInput(in merged);
            }
        }
        catch (OperationCanceledException) when (runCancellation.IsCancellationRequested)
        {
        }
        catch (InvalidOperationException exception) when (!runCancellation.IsCancellationRequested)
        {
            await Console.Error.WriteLineAsync($"xpad input: {exception.Message}").ConfigureAwait(false);
            await Console.Error.WriteLineAsync(
                "xpad input: run `test xpad probe`, then pass `--device-index` or `--motion-device-index`.")
                .ConfigureAwait(false);
            Environment.ExitCode = 1;
        }
        finally
        {
            Console.CancelKeyPress -= OnCancel;
            await PauseIfRequestedAsync(pause, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task RunAllInputAsync(
        SdlGamepadOptions options,
        TimeSpan wait,
        bool noMotion,
        bool pause,
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
            if (options.MotionDeviceIndex.HasValue)
            {
                throw new InvalidOperationException("--motion-device-index is only valid when reading one gamepad.");
            }

            IReadOnlyList<SdlGamepadInfo> gamepads = await WaitForGamepadsAsync(wait, runCancellation.Token)
                .ConfigureAwait(false);
            if (gamepads.Count == 0)
            {
                await Console.Out.WriteLineAsync("no SDL gamepads found").ConfigureAwait(false);
                return;
            }

            List<SdlGamepadSource> inputs = new(gamepads.Count);
            try
            {
                foreach (SdlGamepadInfo gamepad in gamepads)
                {
                    SdlGamepadSource input = await SdlGamepadSource.ConnectAsync(
                        new SdlGamepadOptions
                        {
                            DeviceIndex = gamepad.Index,
                        },
                        runCancellation.Token).ConfigureAwait(false);
                    input.MotionEnabled = !noMotion;
                    inputs.Add(input);

                    await Console.Out.WriteLineAsync(
                        $"xpad input: {DisplayGamepadName(input.DeviceName, input.IsSteamInput)} " +
                        $"index={input.DeviceIndex} gyro={FormatBool(input.HasGyro)} accel={FormatBool(input.HasAccelerometer)}")
                        .ConfigureAwait(false);
                }

                XpadInputPrinter printer = new(includeSource: true);
                await Console.Out.WriteLineAsync("xpad input: reading all SDL gamepads. Ctrl+C to stop.")
                    .ConfigureAwait(false);
                SdlGamepadSource.RunAll(inputs, printer.HandleInput, runCancellation.Token);
            }
            finally
            {
                foreach (SdlGamepadSource input in inputs)
                {
                    await input.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (runCancellation.IsCancellationRequested)
        {
        }
        catch (InvalidOperationException exception) when (!runCancellation.IsCancellationRequested)
        {
            await Console.Error.WriteLineAsync($"xpad input: {exception.Message}").ConfigureAwait(false);
            Environment.ExitCode = 1;
        }
        finally
        {
            Console.CancelKeyPress -= OnCancel;
            await PauseIfRequestedAsync(pause, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task RunMotionSidecarAsync(
        SdlGamepadInfo steamGamepad,
        int? deviceIndex,
        TimeSpan wait,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<SdlGamepadInfo> gamepads = await WaitForGamepadsAsync(wait, cancellationToken)
            .ConfigureAwait(false);
        int motionDeviceIndex = deviceIndex ?? (
            SdlGamepadMotionSelector.TryFindSteamPhysicalMotionCounterpart(gamepads, steamGamepad, out SdlGamepadInfo motionDevice)
                ? motionDevice.Index
                : throw new InvalidOperationException("No strict physical SDL motion counterpart was found."));

        using SdlGamepadSource input = await SdlGamepadSource.ConnectAsync(
            new SdlGamepadOptions
            {
                DeviceIndex = motionDeviceIndex,
            },
            cancellationToken).ConfigureAwait(false);
        input.MotionEnabled = true;
        input.Run(HandleInput, cancellationToken: cancellationToken);

        static void HandleInput(in GamepadInput source)
        {
            GamepadMotion motion = source.State.Motion;
            if (motion.HasGyro || motion.HasAccelerometer)
            {
                Console.WriteLine(FormatMotionLine(motion));
            }
        }
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

        await Console.Out.WriteLineAsync("index  source    steamHandle         vid   pid   gyro  accel  name").ConfigureAwait(false);
        foreach (SdlGamepadInfo gamepad in gamepads)
        {
            await Console.Out.WriteLineAsync(
                $"{gamepad.Index,5}  {DisplaySource(gamepad),-8}  {FormatSteamHandle(gamepad.SteamHandle),18}  " +
                $"{FormatUsbId(gamepad.VendorId),4}  {FormatUsbId(gamepad.ProductId),4}  " +
                $"{FormatBool(gamepad.HasGyro),5}  {FormatBool(gamepad.HasAccelerometer),5}  {gamepad.Name}")
                .ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(gamepad.Path))
            {
                await Console.Out.WriteLineAsync($"       path={gamepad.Path}").ConfigureAwait(false);
            }
        }
    }

    private static async Task<IReadOnlyList<SdlGamepadInfo>> WaitForGamepadsAsync(
        TimeSpan wait,
        CancellationToken cancellationToken)
    {
        DateTimeOffset stopAt = DateTimeOffset.UtcNow + wait;
        IReadOnlyList<SdlGamepadInfo> gamepads;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            gamepads = SdlGamepadSource.GetGamepads();
            if (gamepads.Count > 0 || wait == TimeSpan.Zero)
            {
                return gamepads;
            }

            await Task.Delay(ProbeRetryInterval, cancellationToken).ConfigureAwait(false);
        }
        while (DateTimeOffset.UtcNow < stopAt);

        return gamepads;
    }

    private static async Task<SdlGamepadSource> ConnectWithWaitAsync(
        SdlGamepadOptions options,
        TimeSpan wait,
        CancellationToken cancellationToken)
    {
        DateTimeOffset stopAt = DateTimeOffset.UtcNow + wait;
        InvalidOperationException? lastException;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await SdlGamepadSource.ConnectAsync(options, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException exception)
            {
                lastException = exception;
                if (wait == TimeSpan.Zero)
                {
                    throw;
                }
            }

            await Task.Delay(ProbeRetryInterval, cancellationToken).ConfigureAwait(false);
        }
        while (DateTimeOffset.UtcNow < stopAt);

        throw lastException;
    }

    private static bool ContainsGamepad(IReadOnlyList<SdlGamepadInfo> gamepads, int deviceIndex)
    {
        foreach (SdlGamepadInfo gamepad in gamepads)
        {
            if (gamepad.Index == deviceIndex)
            {
                return true;
            }
        }

        return false;
    }

    private static async Task PauseIfRequestedAsync(bool pause, CancellationToken cancellationToken)
    {
        if (!pause)
        {
            return;
        }

        await Console.Out.WriteLineAsync("press Enter to exit").ConfigureAwait(false);
        while (!cancellationToken.IsCancellationRequested && Console.ReadKey(intercept: true).Key != ConsoleKey.Enter)
        {
        }
    }

    private static string DisplaySource(SdlGamepadInfo gamepad)
    {
        return DisplaySource(gamepad.IsSteamInput);
    }

    private static string DisplaySource(bool isSteamInput)
    {
        return isSteamInput ? "steam" : "physical";
    }

    private static string FormatSteamHandle(ulong steamHandle)
    {
        return steamHandle == 0
            ? "0"
            : $"0x{steamHandle:x16}";
    }

    private static string FormatUsbId(ushort value)
    {
        return value == 0
            ? "----"
            : $"{value:x4}";
    }

    private static string FormatBool(bool value)
    {
        return value ? "true" : "false";
    }

    private static string DisplayGamepadName(string name, bool isSteamInput)
    {
        return isSteamInput ? $"{name} (steam)" : name;
    }

    private static string FormatMotionDevice(SdlGamepadSource input, MotionSidecar? sidecar)
    {
        return input.UsesPhysicalMotion
            ? "local"
            : sidecar is null ? "false" : "sidecar";
    }

    private static GamepadInput MergeMotion(in GamepadInput input, GamepadMotion? motion)
    {
        return motion.HasValue
            ? input with
            {
                State = input.State with
                {
                    Motion = motion.Value,
                },
            }
            : input;
    }

    private static string FormatMotionLine(GamepadMotion motion)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"motion gyro={motion.HasGyro} gx={motion.GyroX:R} gy={motion.GyroY:R} gz={motion.GyroZ:R} accel={motion.HasAccelerometer} ax={motion.AccelX:R} ay={motion.AccelY:R} az={motion.AccelZ:R}");
    }

    private static bool TryParseMotionLine(string line, out GamepadMotion motion)
    {
        motion = default;
        string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 9 ||
            !string.Equals(parts[0], "motion", StringComparison.Ordinal) ||
            !TryReadBool(parts[1], "gyro=", out bool hasGyro) ||
            !TryReadFloat(parts[2], "gx=", out float gyroX) ||
            !TryReadFloat(parts[3], "gy=", out float gyroY) ||
            !TryReadFloat(parts[4], "gz=", out float gyroZ) ||
            !TryReadBool(parts[5], "accel=", out bool hasAccelerometer) ||
            !TryReadFloat(parts[6], "ax=", out float accelX) ||
            !TryReadFloat(parts[7], "ay=", out float accelY) ||
            !TryReadFloat(parts[8], "az=", out float accelZ))
        {
            return false;
        }

        motion = new GamepadMotion(
            hasGyro,
            gyroX,
            gyroY,
            gyroZ,
            hasAccelerometer,
            accelX,
            accelY,
            accelZ);
        return true;
    }

    private static bool TryReadBool(string value, string prefix, out bool result)
    {
        result = default;
        return value.StartsWith(prefix, StringComparison.Ordinal) &&
            bool.TryParse(value[prefix.Length..], out result);
    }

    private static bool TryReadFloat(string value, string prefix, out float result)
    {
        result = default;
        return value.StartsWith(prefix, StringComparison.Ordinal) &&
            float.TryParse(value[prefix.Length..], NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    }

    private static MotionSidecar StartMotionSidecar(
        SdlGamepadInfo steamGamepad,
        int? motionDeviceIndex,
        TimeSpan wait,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = CreateCurrentCommandStartInfo();
        startInfo.ArgumentList.Add("test");
        startInfo.ArgumentList.Add("xpad");
        startInfo.ArgumentList.Add("motion-sidecar");
        startInfo.ArgumentList.Add("--name");
        startInfo.ArgumentList.Add(steamGamepad.Name);
        startInfo.ArgumentList.Add("--vid");
        startInfo.ArgumentList.Add(steamGamepad.VendorId.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--pid");
        startInfo.ArgumentList.Add(steamGamepad.ProductId.ToString(CultureInfo.InvariantCulture));
        if (motionDeviceIndex.HasValue)
        {
            startInfo.ArgumentList.Add("--device-index");
            startInfo.ArgumentList.Add(motionDeviceIndex.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (wait > TimeSpan.Zero)
        {
            startInfo.ArgumentList.Add("--wait-ms");
            startInfo.ArgumentList.Add(((int)wait.TotalMilliseconds).ToString(CultureInfo.InvariantCulture));
        }

        RemoveSteamEnvironment(startInfo);
        Process process = Process.Start(startInfo) ??
            throw new InvalidOperationException("Could not start SDL physical motion sidecar.");
        return new MotionSidecar(process, cancellationToken);
    }

    private static ProcessStartInfo CreateCurrentCommandStartInfo()
    {
        (string fileName, string? assemblyPath) = GetCurrentCommand();
        ProcessStartInfo startInfo = new()
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        if (assemblyPath is not null)
        {
            startInfo.ArgumentList.Add(assemblyPath);
        }

        return startInfo;
    }

    private static (string FileName, string? AssemblyPath) GetCurrentCommand()
    {
        string? processPath = Environment.ProcessPath;
        bool isDotNetHost = string.Equals(
            Path.GetFileNameWithoutExtension(processPath),
            "dotnet",
            StringComparison.OrdinalIgnoreCase);

        string assemblyPath = string.IsNullOrWhiteSpace(typeof(XpadCommands).Assembly.Location)
            ? throw new InvalidOperationException("Could not resolve the CLI assembly path.")
            : typeof(XpadCommands).Assembly.Location;

        return !string.IsNullOrWhiteSpace(processPath) && !isDotNetHost
            ? (processPath, null)
            : (string.IsNullOrWhiteSpace(processPath) ? "dotnet" : processPath, assemblyPath);
    }

    private static void RemoveSteamEnvironment(ProcessStartInfo startInfo)
    {
        List<string> keys = [];
        foreach (string key in startInfo.Environment.Keys)
        {
            if (key.Contains("steam", StringComparison.OrdinalIgnoreCase))
            {
                keys.Add(key);
            }
        }

        foreach (string key in keys)
        {
            _ = startInfo.Environment.Remove(key);
        }
    }

    private static string DisplayVector(bool hasValue, float x, float y, float z)
    {
        return hasValue
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{x:0.###},{y:0.###},{z:0.###}")
            : "none";
    }

    private sealed class XpadInputPrinter(string? displayDeviceName = null, bool includeSource = false)
    {
        private static readonly long MinPrintIntervalTicks = Stopwatch.Frequency / 10;
        private readonly Dictionary<int, long> lastPrintTimestamps = [];
        private long lastPrintTimestamp;

        public void HandleInput(SdlGamepadSource source, GamepadInput input)
        {
            HandleInputCore(source, input);
        }

        public void HandleInput(in GamepadInput input)
        {
            HandleInputCore(source: null, input);
        }

        private void HandleInputCore(SdlGamepadSource? source, in GamepadInput input)
        {
            long timestamp = Stopwatch.GetTimestamp();
            long previousTimestamp = GetLastPrintTimestamp(source);
            if (previousTimestamp != 0 && timestamp - previousTimestamp < MinPrintIntervalTicks)
            {
                return;
            }

            SetLastPrintTimestamp(source, timestamp);

            GamepadState state = input.State;
            Console.WriteLine(
                FormatPrefix(source) +
                $"device=\"{displayDeviceName ?? input.DeviceName}\" " +
                $"buttons={DisplayButtons(state.Buttons)} " +
                $"lx={state.LeftX} ly={state.LeftY} rx={state.RightX} ry={state.RightY} " +
                $"lt={state.LeftTrigger} rt={state.RightTrigger} " +
                DisplayMotion(state.Motion));
        }

        private string FormatPrefix(SdlGamepadSource? source)
        {
            return includeSource && source is not null
                ? $"index={source.DeviceIndex} {DisplayGamepadName(source.DeviceName, source.IsSteamInput)} "
                : string.Empty;
        }

        private long GetLastPrintTimestamp(SdlGamepadSource? source)
        {
            return source is null
                ? lastPrintTimestamp
                : lastPrintTimestamps.GetValueOrDefault(source.DeviceIndex);
        }

        private void SetLastPrintTimestamp(SdlGamepadSource? source, long timestamp)
        {
            if (source is null)
            {
                lastPrintTimestamp = timestamp;
                return;
            }

            lastPrintTimestamps[source.DeviceIndex] = timestamp;
        }
    }

    private sealed class MotionSidecar : IDisposable
    {
        private readonly Process process;
        private readonly CancellationTokenSource cancellation;
        private readonly Task stdoutTask;
        private readonly Task stderrTask;

        public MotionSidecar(Process process, CancellationToken cancellationToken)
        {
            this.process = process;
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            stdoutTask = ReadStdoutAsync(cancellation.Token);
            stderrTask = ReadStderrAsync(cancellation.Token);
        }

        public GamepadMotion? LatestMotion { get; private set; }

        public void Dispose()
        {
            cancellation.Cancel();
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }

                process.WaitForExit();
                _ = Task.WaitAll([stdoutTask, stderrTask], TimeSpan.FromSeconds(1));
            }
            catch (InvalidOperationException)
            {
            }
            catch (AggregateException)
            {
            }
            finally
            {
                cancellation.Dispose();
                process.Dispose();
            }
        }

        private async Task ReadStdoutAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    string? line = await process.StandardOutput.ReadLineAsync(cancellationToken)
                        .ConfigureAwait(false);
                    if (line is null)
                    {
                        return;
                    }

                    if (TryParseMotionLine(line, out GamepadMotion motion))
                    {
                        LatestMotion = motion;
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (IOException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }

        private async Task ReadStderrAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    string? line = await process.StandardError.ReadLineAsync(cancellationToken)
                        .ConfigureAwait(false);
                    if (line is null)
                    {
                        return;
                    }

                    await Console.Error.WriteLineAsync($"xpad motion: {line}").ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (IOException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }
    }
}
