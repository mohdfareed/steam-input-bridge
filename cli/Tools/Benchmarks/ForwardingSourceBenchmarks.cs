using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Inputs;
using Inputs.RawInput;
using Inputs.Sdl;

namespace Cli.Tools.Benchmarks;

internal static partial class ForwardingBenchmarks
{
    /// <summary>Measures Raw Input API read/decode to callback.</summary>
    [SupportedOSPlatform("windows")]
    internal static async Task<ForwardingBenchmarkMeasurement> BenchmarkRawInputAsync(
        int count,
        IProgress<ForwardingBenchmarkProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfInvalidCount(count);

        using RawInputMouseSource input = await RawInputMouseSource
            .ConnectAsync(cancellationToken)
            .ConfigureAwait(false);
        using CancellationTokenSource runCancellation = CancellationTokenSource
            .CreateLinkedTokenSource(cancellationToken);

        long[] samples = new long[count];
        int warmupCount = 0;
        int sampleCount = 0;
        long totalElapsed = 0;
        Task progressTask = ReportProgressAsync(
            () => Volatile.Read(ref warmupCount),
            () => Volatile.Read(ref sampleCount),
            count,
            progress,
            runCancellation.Token);

        try
        {
            input.Run(HandleInput, HandleTiming, runCancellation.Token);
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            await runCancellation.CancelAsync().ConfigureAwait(false);
            await progressTask.ConfigureAwait(false);
        }

        return sampleCount < count
            ? throw new InvalidOperationException("Raw Input benchmark stopped before collecting enough reports.")
            : new ForwardingBenchmarkMeasurement(count, totalElapsed, samples, -1);

        static void HandleInput(in MouseInput input)
        {
            _ = input;
        }

        void HandleTiming(long startedTimestamp, long emittedTimestamp)
        {
            CollectSample(
                startedTimestamp,
                emittedTimestamp,
                count,
                samples,
                ref warmupCount,
                ref sampleCount,
                ref totalElapsed,
                runCancellation);
        }
    }

    /// <summary>Measures SDL update/read to callback.</summary>
    internal static async Task<ForwardingBenchmarkMeasurement> BenchmarkSdlInputAsync(
        int count,
        IProgress<ForwardingBenchmarkProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfInvalidCount(count);

        IReadOnlyList<SdlControllerInfo> controllers = SdlControllerCatalog.GetControllers();
        if (controllers.Count == 0)
        {
            throw new InvalidOperationException("No SDL gamepads found.");
        }

        using SdlGamepadSource input = await SdlGamepadSource
            .ConnectAsync(controllers[0], cancellationToken)
            .ConfigureAwait(false);
        using CancellationTokenSource runCancellation = CancellationTokenSource
            .CreateLinkedTokenSource(cancellationToken);

        long[] samples = new long[count];
        int warmupCount = 0;
        int sampleCount = 0;
        long totalElapsed = 0;
        long previousTimestamp = Stopwatch.GetTimestamp();
        Task progressTask = ReportProgressAsync(
            () => Volatile.Read(ref warmupCount),
            () => Volatile.Read(ref sampleCount),
            count,
            progress,
            runCancellation.Token);

        try
        {
            input.Run(HandleInput, runCancellation.Token);
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            await runCancellation.CancelAsync().ConfigureAwait(false);
            await progressTask.ConfigureAwait(false);
        }

        return sampleCount < count
            ? throw new InvalidOperationException("SDL benchmark stopped before collecting enough reports.")
            : new ForwardingBenchmarkMeasurement(count, totalElapsed, samples, -1);

        void HandleInput(in GamepadInput input)
        {
            _ = input;
            long emittedTimestamp = Stopwatch.GetTimestamp();
            CollectSample(
                previousTimestamp,
                emittedTimestamp,
                count,
                samples,
                ref warmupCount,
                ref sampleCount,
                ref totalElapsed,
                runCancellation);
            previousTimestamp = emittedTimestamp;
        }
    }

    private static async Task ReportProgressAsync(
        Func<int> getWarmupCount,
        Func<int> getSampleCount,
        int count,
        IProgress<ForwardingBenchmarkProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (progress is null)
        {
            return;
        }

        using PeriodicTimer timer = new(ProgressInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                progress.Report(new ForwardingBenchmarkProgress(
                    Math.Min(getWarmupCount(), WarmupCount),
                    WarmupCount,
                    Math.Min(getSampleCount(), count),
                    count));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}
