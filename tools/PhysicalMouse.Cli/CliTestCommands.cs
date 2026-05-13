using System;
using System.CommandLine;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using PhysicalMouse;
using PhysicalMouse.Viiper;

internal static class CliTestCommands
{
    private static readonly int[] SmokePresetDpis = [400, 800, 1600, 3200, 100, 6400];

    private const double SmokeTravelInches = 0.25;
    private const int SmokeMinSteps = 120;
    private const int SmokeStepDelayMs = 2;

    // MARK: Commands
    // ========================================================================

    internal static Command CreateBenchCommand()
    {
        Command command = new("bench", "Measure send-path cost over many reports.");

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            const int count = 10_000;
            const int warmup = 1_000;
            const int dx = 1;
            const int dy = 0;
            const int wheel = 0;
            MouseReport report = new(MouseButtons.None, dx, dy, wheel);

            _ = await CliConnection.ExecuteAsync(
                async (mouse, ct) =>
                {
                    await CliConnection.PrintConnectionAsync(mouse).ConfigureAwait(false);

                    for (int i = 0; i < warmup; i++)
                    {
                        await mouse.SendAsync(report, ct).ConfigureAwait(false);
                    }

                    long[] samples = new long[count];
                    long totalStart = Stopwatch.GetTimestamp();

                    for (int i = 0; i < count; i++)
                    {
                        long start = Stopwatch.GetTimestamp();
                        await mouse.SendAsync(report, ct).ConfigureAwait(false);
                        samples[i] = Stopwatch.GetTimestamp() - start;
                    }

                    long totalElapsed = Stopwatch.GetTimestamp() - totalStart;
                    await PrintBenchmarkAsync(count, warmup, totalElapsed, samples).ConfigureAwait(false);
                    return 0;
                },
                cancellationToken).ConfigureAwait(false);
        });

        return command;
    }

    internal static Command CreateSmokeCommand()
    {
        Command command = new("smoke", "Run a horizontal sweep for visual checking.");
        Option<int?> dpiOption = new("--dpi")
        {
            Description = "Lock to one DPI and loop until Ctrl+C.",
        };

        command.Options.Add(dpiOption);

        dpiOption.Validators.Add(result =>
        {
            int? dpi = result.GetValue(dpiOption);
            if (dpi.HasValue && dpi.Value < 1)
            {
                result.AddError("--dpi must be greater than 0.");
            }
        });

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            int? dpi = parseResult.GetValue(dpiOption);

            _ = await CliConnection.ExecuteAsync(
                async (mouse, ct) =>
                {
                    await CliConnection.PrintConnectionAsync(mouse).ConfigureAwait(false);

                    if (dpi.HasValue)
                    {
                        await Console.Out.WriteLineAsync($"DPI {dpi.Value}. Press Ctrl+C to stop.").ConfigureAwait(false);
                        while (!ct.IsCancellationRequested)
                        {
                            await RunSmokeAsync(mouse, dpi.Value, ct).ConfigureAwait(false);
                        }

                        return 0;
                    }

                    foreach (int presetDpi in SmokePresetDpis)
                    {
                        await Console.Out.WriteLineAsync($"DPI {presetDpi}.").ConfigureAwait(false);
                        await RunSmokeAsync(mouse, presetDpi, ct).ConfigureAwait(false);
                    }

                    await Console.Out.WriteLineAsync("Smoke OK.").ConfigureAwait(false);
                    return 0;
                },
                cancellationToken).ConfigureAwait(false);
        });

        return command;
    }

    // MARK: Helpers
    // ========================================================================

    private static async Task RunSmokeAsync(
        ViiperPhysicalMouse mouse,
        int dpi,
        CancellationToken cancellationToken)
    {
        int distance = Math.Max(1, (int)Math.Round(dpi * SmokeTravelInches));
        int steps = Math.Max(SmokeMinSteps, distance);
        await MoveLinearAsync(mouse, distance, steps, cancellationToken).ConfigureAwait(false);
        await MoveLinearAsync(mouse, -distance, steps, cancellationToken).ConfigureAwait(false);
    }

    private static async Task MoveLinearAsync(
        ViiperPhysicalMouse mouse,
        int totalDelta,
        int steps,
        CancellationToken cancellationToken)
    {
        int sent = 0;

        for (int step = 1; step <= steps; step++)
        {
            int target = (int)Math.Round(totalDelta * step / (double)steps);
            int delta = target - sent;
            if (delta != 0)
            {
                await mouse.SendAsync(new MouseReport(MouseButtons.None, delta, 0, 0), cancellationToken).ConfigureAwait(false);
                sent = target;
            }

            await Task.Delay(SmokeStepDelayMs, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task PrintBenchmarkAsync(int count, int warmup, long totalElapsed, long[] samples)
    {
        Array.Sort(samples);

        double totalMs = ToMilliseconds(totalElapsed);
        double averageUs = ToMicroseconds(totalElapsed) / count;
        double p50Us = ToMicroseconds(samples[count / 2]);
        double p95Us = ToMicroseconds(samples[(int)Math.Clamp(Math.Ceiling(count * 0.95) - 1, 0, count - 1)]);
        double p99Us = ToMicroseconds(samples[(int)Math.Clamp(Math.Ceiling(count * 0.99) - 1, 0, count - 1)]);
        double maxUs = ToMicroseconds(samples[count - 1]);
        double sendsPerSecond = count / (totalMs / 1000.0);

        await Console.Out.WriteLineAsync($"Warmup: {warmup}").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"Count: {count}").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"TotalMs: {totalMs:F3}").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"AverageUs: {averageUs:F3}").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"P50Us: {p50Us:F3}").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"P95Us: {p95Us:F3}").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"P99Us: {p99Us:F3}").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"MaxUs: {maxUs:F3}").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"SendsPerSecond: {sendsPerSecond:F0}").ConfigureAwait(false);
    }

    private static double ToMilliseconds(long ticks)
    {
        return ticks * 1000.0 / Stopwatch.Frequency;
    }

    private static double ToMicroseconds(long ticks)
    {
        return ticks * 1_000_000.0 / Stopwatch.Frequency;
    }
}
