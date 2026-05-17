using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inputs;
using Inputs.Sdl;

namespace Cli.Tools.Benchmarks;

/// <summary>Benchmarks source-to-output forwarding boundaries.</summary>
internal static partial class ForwardingBenchmarks
{
    /// <summary>Default measured report count.</summary>
    public const int DefaultCount = 1_000;

    /// <summary>Default warmup report count.</summary>
    public const int WarmupCount = 1_000;

    private static readonly TimeSpan ProgressInterval = TimeSpan.FromSeconds(1);

    /// <summary>Runs the requested benchmark pair.</summary>
    public static async Task<IReadOnlyList<ForwardingBenchmarkResult>> RunAsync(
        ForwardingBenchmarkInput input,
        ForwardingBenchmarkOutput output,
        int count,
        IProgress<ForwardingBenchmarkProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfInvalidCount(count);

        return (input, output) switch
        {
            (_, ForwardingBenchmarkOutput.Teensy) => throw new NotSupportedException(
                "Teensy output is not implemented."),
            (ForwardingBenchmarkInput.Raw, ForwardingBenchmarkOutput.Viiper) => await RunRawToViiperAsync(
                count,
                progress,
                cancellationToken)
                .ConfigureAwait(false),
            (ForwardingBenchmarkInput.Sdl, ForwardingBenchmarkOutput.Viiper) => await RunSdlToViiperAsync(
                count,
                progress,
                cancellationToken)
                .ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(input)),
        };
    }

    private static async Task<IReadOnlyList<ForwardingBenchmarkResult>> RunRawToViiperAsync(
        int count,
        IProgress<ForwardingBenchmarkProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Raw Input benchmarks require Windows.");
        }

        ForwardingBenchmarkMeasurement inputMeasurement = await BenchmarkRawInputAsync(
            count,
            progress,
            cancellationToken).ConfigureAwait(false);
        MouseReport report = new(MouseButtons.None, 1, 0, 0);
        ForwardingBenchmarkMeasurement bridgeMeasurement = BenchmarkSourceToViiperApi(
            report,
            count,
            cancellationToken);

        return
        [
            new ForwardingBenchmarkResult("test mouse bench input api->callback", inputMeasurement),
            new ForwardingBenchmarkResult("test mouse bench viiper bridge source->viiper-api", bridgeMeasurement),
        ];
    }

    private static async Task<IReadOnlyList<ForwardingBenchmarkResult>> RunSdlToViiperAsync(
        int count,
        IProgress<ForwardingBenchmarkProgress>? progress,
        CancellationToken cancellationToken)
    {
        ForwardingBenchmarkMeasurement inputMeasurement = await BenchmarkSdlInputAsync(
            count,
            new SdlGamepadOptions(),
            progress,
            cancellationToken).ConfigureAwait(false);
        GamepadState state = new(
            GamepadButtons.South,
            1,
            -2,
            3,
            -4,
            32767,
            16384,
            default);
        ForwardingBenchmarkMeasurement bridgeMeasurement = BenchmarkGamepadToViiperApi(
            state,
            count,
            cancellationToken);

        return
        [
            new ForwardingBenchmarkResult("test xpad bench input api->callback", inputMeasurement),
            new ForwardingBenchmarkResult("test xpad bench viiper bridge source->viiper-api", bridgeMeasurement),
        ];
    }
}
