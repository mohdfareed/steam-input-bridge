using System;
using System.Threading;
using System.Threading.Tasks;
using Inputs;
using Outputs;

namespace Cli.Tools.Benchmarks;

internal static partial class ForwardingBenchmarks
{
    /// <summary>Measures mouse source callback to VIIPER mouse API input mapping.</summary>
    internal static ForwardingBenchmarkMeasurement BenchmarkSourceToViiperApi(
        MouseReport report,
        int count,
        CancellationToken cancellationToken = default)
    {
        ThrowIfInvalidCount(count);

        MouseInput input = new(report, string.Empty);
        BenchmarkTiming warmupTiming = new(null);
        BenchmarkViiperMouseApi warmupViiperApi = new(warmupTiming);
        using BenchmarkMouseOutput warmupMouse = new(warmupViiperApi);
        using BenchmarkMouseSource warmupSource = new(input, WarmupCount, warmupTiming);
        RunMouseBridge(warmupSource, warmupMouse, cancellationToken);

        long[] samples = new long[count];
        BenchmarkTiming timing = new(samples);
        BenchmarkViiperMouseApi viiperApi = new(timing);
        using BenchmarkMouseOutput mouse = new(viiperApi);
        using BenchmarkMouseSource source = new(input, count, timing);
        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: false);
        RunMouseBridge(source, mouse, cancellationToken);

        long allocatedBytes = GC.GetTotalAllocatedBytes(precise: false) - allocatedBefore;
        return new ForwardingBenchmarkMeasurement(count, timing.TotalElapsed, samples, allocatedBytes);
    }

    /// <summary>Measures gamepad source callback to VIIPER Xbox 360 API input mapping.</summary>
    internal static ForwardingBenchmarkMeasurement BenchmarkGamepadToViiperApi(
        GamepadState state,
        int count,
        CancellationToken cancellationToken = default)
    {
        ThrowIfInvalidCount(count);

        BenchmarkTiming warmupTiming = new(null);
        using BenchmarkGamepadSource warmupSource = new(state, WarmupCount, warmupTiming);
        using BenchmarkXbox360Output warmupOutput = new(warmupTiming);
        RunGamepadBridge(warmupSource, warmupOutput, cancellationToken);

        long[] samples = new long[count];
        BenchmarkTiming timing = new(samples);
        using BenchmarkGamepadSource source = new(state, count, timing);
        using BenchmarkXbox360Output output = new(timing);
        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: false);
        RunGamepadBridge(source, output, cancellationToken);

        long allocatedBytes = GC.GetTotalAllocatedBytes(precise: false) - allocatedBefore;
        return new ForwardingBenchmarkMeasurement(count, timing.TotalElapsed, samples, allocatedBytes);
    }

    private static void RunMouseBridge(
        IMouseInputSource source,
        IMouseOutput output,
        CancellationToken cancellationToken)
    {
        source.Run(HandleInput, cancellationToken);

        void HandleInput(in MouseInput input)
        {
            if (output.FilterInput(input.DeviceName))
            {
                SendSynchronously(output.SendAsync(input.Report, cancellationToken));
            }
        }
    }

    private static void RunGamepadBridge(
        IGamepadInputSource source,
        IXbox360Output output,
        CancellationToken cancellationToken)
    {
        source.Run(HandleInput, cancellationToken);

        void HandleInput(in GamepadInput input)
        {
            Xbox360Report report = ToXbox360Report(input.State);
            SendSynchronously(output.SendAsync(report, cancellationToken));
        }
    }

    private static void SendSynchronously(ValueTask sendTask)
    {
        if (sendTask.IsCompleted)
        {
            sendTask.GetAwaiter().GetResult();
            return;
        }

        sendTask.AsTask().GetAwaiter().GetResult();
    }

    private static Xbox360Report ToXbox360Report(GamepadState state)
    {
        Xbox360Buttons buttons = Xbox360Buttons.None;
        buttons = Apply(buttons, state.Buttons, GamepadButtons.South, Xbox360Buttons.A);
        buttons = Apply(buttons, state.Buttons, GamepadButtons.East, Xbox360Buttons.B);
        buttons = Apply(buttons, state.Buttons, GamepadButtons.West, Xbox360Buttons.X);
        buttons = Apply(buttons, state.Buttons, GamepadButtons.North, Xbox360Buttons.Y);
        buttons = Apply(buttons, state.Buttons, GamepadButtons.Back, Xbox360Buttons.Back);
        buttons = Apply(buttons, state.Buttons, GamepadButtons.Guide, Xbox360Buttons.Guide);
        buttons = Apply(buttons, state.Buttons, GamepadButtons.Start, Xbox360Buttons.Start);
        buttons = Apply(buttons, state.Buttons, GamepadButtons.LeftStick, Xbox360Buttons.LeftThumb);
        buttons = Apply(buttons, state.Buttons, GamepadButtons.RightStick, Xbox360Buttons.RightThumb);
        buttons = Apply(buttons, state.Buttons, GamepadButtons.LeftShoulder, Xbox360Buttons.LeftShoulder);
        buttons = Apply(buttons, state.Buttons, GamepadButtons.RightShoulder, Xbox360Buttons.RightShoulder);
        buttons = Apply(buttons, state.Buttons, GamepadButtons.DPadUp, Xbox360Buttons.DPadUp);
        buttons = Apply(buttons, state.Buttons, GamepadButtons.DPadDown, Xbox360Buttons.DPadDown);
        buttons = Apply(buttons, state.Buttons, GamepadButtons.DPadLeft, Xbox360Buttons.DPadLeft);
        buttons = Apply(buttons, state.Buttons, GamepadButtons.DPadRight, Xbox360Buttons.DPadRight);

        return new Xbox360Report(
            buttons,
            ToByteTrigger(state.LeftTrigger),
            ToByteTrigger(state.RightTrigger),
            state.LeftX,
            InvertAxis(state.LeftY),
            state.RightX,
            InvertAxis(state.RightY));
    }

    private static Xbox360Buttons Apply(
        Xbox360Buttons output,
        GamepadButtons input,
        GamepadButtons inputButton,
        Xbox360Buttons outputButton)
    {
        return (input & inputButton) != 0 ? output | outputButton : output;
    }

    private static byte ToByteTrigger(ushort value)
    {
        return (byte)Math.Clamp(value * 255 / 32767, byte.MinValue, byte.MaxValue);
    }

    private static short InvertAxis(short value)
    {
        return value == short.MinValue ? short.MaxValue : (short)-value;
    }
}
