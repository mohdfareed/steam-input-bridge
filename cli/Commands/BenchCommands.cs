using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Cli.Tools.Benchmarks;

internal static class BenchCommands
{
    // MARK: Commands
    // ========================================================================

    internal static Command CreateBenchCommand(ForwardingBenchmarkInput? fixedInput = null)
    {
        Command command = new("bench", "Measure source-to-output forwarding cost.");
        Argument<ForwardingBenchmarkOutput> outputArgument = new("output")
        {
            Description = "Output transport: viiper or teensy.",
        };
        Option<int?> countOption = CliOptions.CreateCountOption(ForwardingBenchmarks.DefaultCount);

        Argument<ForwardingBenchmarkInput>? inputArgument = null;
        if (!fixedInput.HasValue)
        {
            inputArgument = new("input")
            {
                Description = "Input source: raw or sdl.",
            };
            command.Arguments.Add(inputArgument);
        }

        command.Arguments.Add(outputArgument);
        command.Options.Add(countOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            ForwardingBenchmarkInput input = fixedInput ?? parseResult.GetValue(inputArgument!);
            ForwardingBenchmarkOutput output = parseResult.GetValue(outputArgument);
            int count = parseResult.GetValue(countOption) ?? ForwardingBenchmarks.DefaultCount;
            await RunBenchAsync(input, output, count, cancellationToken).ConfigureAwait(false);
        });

        return command;
    }

    internal static async Task RunBenchAsync(
        ForwardingBenchmarkInput input,
        ForwardingBenchmarkOutput output,
        int count,
        CancellationToken cancellationToken)
    {
        try
        {
            await PrintInputPromptAsync(input, output, count).ConfigureAwait(false);
            IReadOnlyList<ForwardingBenchmarkResult> results = await ForwardingBenchmarks
                .RunAsync(input, output, count, new ConsoleBenchmarkProgress(), cancellationToken)
                .ConfigureAwait(false);

            foreach (ForwardingBenchmarkResult result in results)
            {
                await PrintBenchmarkAsync(result).ConfigureAwait(false);
            }
        }
        catch (NotSupportedException ex)
        {
            await Console.Error.WriteLineAsync($"bench: {ex.Message}").ConfigureAwait(false);
            Environment.ExitCode = 1;
        }
    }

    // MARK: Output
    // ========================================================================

    private static async Task PrintInputPromptAsync(
        ForwardingBenchmarkInput input,
        ForwardingBenchmarkOutput output,
        int count)
    {
        if (output != ForwardingBenchmarkOutput.Viiper)
        {
            return;
        }

        string? message = input switch
        {
            ForwardingBenchmarkInput.Raw when OperatingSystem.IsWindows() =>
                $"test mouse bench viiper input: move the mouse until warmup {ForwardingBenchmarks.WarmupCount:N0} + reports {count:N0} are collected.",
            ForwardingBenchmarkInput.Sdl =>
                $"test xpad bench viiper input: move or press the controller until warmup {ForwardingBenchmarks.WarmupCount:N0} + reports {count:N0} are collected.",
            _ => null,
        };

        if (message is not null)
        {
            await Console.Out.WriteLineAsync(message).ConfigureAwait(false);
        }
    }

    private static async Task PrintBenchmarkAsync(ForwardingBenchmarkResult result)
    {
        ForwardingBenchmarkMeasurement measurement = result.Measurement;
        int count = measurement.Count;
        long[] samples = [.. measurement.Samples];
        Array.Sort(samples);

        double totalMs = ToMilliseconds(measurement.TotalElapsed);
        double sendsPerSecond = count / (totalMs / 1000.0);
        double rateMultiple = sendsPerSecond / 1000.0;
        double averageMs = totalMs / count;
        double middleMs = ToMilliseconds(samples[count / 2]);
        double slow95Ms = ToMilliseconds(samples[(int)Math.Clamp(Math.Ceiling(count * 0.95) - 1, 0, count - 1)]);
        double slow99Ms = ToMilliseconds(samples[(int)Math.Clamp(Math.Ceiling(count * 0.99) - 1, 0, count - 1)]);
        double maxMs = ToMilliseconds(samples[count - 1]);
        double allocatedBytesPerSend = measurement.AllocatedBytes / (double)count;

        await Console.Out.WriteLineAsync(result.Title).ConfigureAwait(false);
        await Console.Out.WriteLineAsync(
            $"  reports  {count:N0}  warmup {ForwardingBenchmarks.WarmupCount:N0}")
            .ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"  rate     {sendsPerSecond:N0}/s  ({rateMultiple:N0}x 1000 Hz)")
            .ConfigureAwait(false);
        await Console.Out.WriteLineAsync(
            $"  time     avg {ToMicroseconds(averageMs):F3} us  " +
            $"50% {ToMicroseconds(middleMs):F3} us  " +
            $"95% {ToMicroseconds(slow95Ms):F3} us  " +
            $"99% {ToMicroseconds(slow99Ms):F3} us  " +
            $"max {ToMicroseconds(maxMs):F3} us").ConfigureAwait(false);
        await Console.Out.WriteLineAsync(
            measurement.AllocatedBytes < 0
                ? "  alloc    n/a"
                : $"  alloc    {allocatedBytesPerSend:F1} B/report").ConfigureAwait(false);
    }

    private static double ToMilliseconds(long ticks)
    {
        return ticks * 1000.0 / Stopwatch.Frequency;
    }

    private static double ToMicroseconds(double milliseconds)
    {
        return milliseconds * 1000.0;
    }

    private sealed class ConsoleBenchmarkProgress : IProgress<ForwardingBenchmarkProgress>
    {
        public void Report(ForwardingBenchmarkProgress value)
        {
            Console.Out.WriteLine(
                $"  progress warmup {value.WarmupCount:N0}/{value.WarmupTarget:N0}  " +
                $"reports {value.SampleCount:N0}/{value.SampleTarget:N0}");
        }
    }
}
