using System;
using System.CommandLine;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using PhysicalMouse;
using PhysicalMouse.Viiper;

internal static class CliDiagnosticsCommands
{
    // MARK: Commands
    // ========================================================================

    internal static Command CreateSmokeCommand()
    {
        Command command = new("smoke", "Move out and back with a pause.");
        Option<int> distanceOption = new("--distance")
        {
            Description = "Distance to move before returning.",
            DefaultValueFactory = _ => 50,
        };

        Option<int> pauseMsOption = new("--pause-ms")
        {
            Description = "Pause between the outbound and return move.",
            DefaultValueFactory = _ => 1000,
        };

        command.Options.Add(distanceOption);
        command.Options.Add(pauseMsOption);
        CliConnection.ConnectionOptions options = CliConnection.AddOptions(command);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            int distance = parseResult.GetValue(distanceOption);
            int pauseMs = parseResult.GetValue(pauseMsOption);

            _ = await CliConnection.ExecuteAsync(
                parseResult,
                options,
                async (mouse, ct) =>
                {
                    await CliConnection.PrintConnectionAsync(mouse).ConfigureAwait(false);
                    await mouse.SendAsync(new MouseReport(MouseButtons.None, distance, 0, 0), ct).ConfigureAwait(false);
                    await Task.Delay(pauseMs, ct).ConfigureAwait(false);
                    await mouse.SendAsync(new MouseReport(MouseButtons.None, -distance, 0, 0), ct).ConfigureAwait(false);
                    await Console.Out.WriteLineAsync($"Smoke OK. Moved +{distance}, waited {pauseMs} ms, then moved -{distance}.").ConfigureAwait(false);
                    return 0;
                },
                cancellationToken).ConfigureAwait(false);
        });

        return command;
    }

    internal static Command CreateBenchCommand()
    {
        Command command = new("bench", "Measure send-path cost over many reports.");
        Option<int> countOption = new("--count")
        {
            Description = "Measured send count.",
            DefaultValueFactory = _ => 10_000,
        };

        Option<int> warmupOption = new("--warmup")
        {
            Description = "Warmup send count.",
            DefaultValueFactory = _ => 1_000,
        };

        Option<int> dxOption = new("--dx")
        {
            Description = "Horizontal delta per report.",
            DefaultValueFactory = _ => 1,
        };

        Option<int> dyOption = new("--dy")
        {
            Description = "Vertical delta per report.",
            DefaultValueFactory = _ => 0,
        };

        Option<int> wheelOption = new("--wheel")
        {
            Description = "Wheel delta per report.",
            DefaultValueFactory = _ => 0,
        };

        command.Options.Add(countOption);
        command.Options.Add(warmupOption);
        command.Options.Add(dxOption);
        command.Options.Add(dyOption);
        command.Options.Add(wheelOption);
        CliConnection.ConnectionOptions options = CliConnection.AddOptions(command);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            int count = parseResult.GetValue(countOption);
            int warmup = parseResult.GetValue(warmupOption);
            int dx = parseResult.GetValue(dxOption);
            int dy = parseResult.GetValue(dyOption);
            int wheel = parseResult.GetValue(wheelOption);
            MouseReport report = new(MouseButtons.None, dx, dy, wheel);

            _ = await CliConnection.ExecuteAsync(
                parseResult,
                options,
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

    internal static Command CreateSmoothCommand()
    {
        Command command = new("smooth", "Draw a slow circle for visual checking.");
        Option<int> radiusOption = new("--radius")
        {
            Description = "Circle radius in pixels.",
            DefaultValueFactory = _ => 80,
        };

        Option<int> stepsOption = new("--steps")
        {
            Description = "Number of reports in the circle.",
            DefaultValueFactory = _ => 240,
        };

        Option<int> durationMsOption = new("--duration-ms")
        {
            Description = "Total circle duration.",
            DefaultValueFactory = _ => 4000,
        };

        command.Options.Add(radiusOption);
        command.Options.Add(stepsOption);
        command.Options.Add(durationMsOption);
        CliConnection.ConnectionOptions options = CliConnection.AddOptions(command);

        stepsOption.Validators.Add(result =>
        {
            if (result.GetValue(stepsOption) < 4)
            {
                result.AddError("--steps must be at least 4.");
            }
        });

        durationMsOption.Validators.Add(result =>
        {
            if (result.GetValue(durationMsOption) < 1)
            {
                result.AddError("--duration-ms must be greater than 0.");
            }
        });

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            int radius = parseResult.GetValue(radiusOption);
            int steps = parseResult.GetValue(stepsOption);
            int durationMs = parseResult.GetValue(durationMsOption);

            _ = await CliConnection.ExecuteAsync(
                parseResult,
                options,
                async (mouse, ct) =>
                {
                    await CliConnection.PrintConnectionAsync(mouse).ConfigureAwait(false);
                    await DrawCircleAsync(mouse, radius, steps, durationMs, ct).ConfigureAwait(false);
                    await Console.Out.WriteLineAsync($"Smooth OK. Drew a circle with radius {radius}, {steps} steps, over {durationMs} ms.").ConfigureAwait(false);
                    return 0;
                },
                cancellationToken).ConfigureAwait(false);
        });

        return command;
    }

    // MARK: Helpers
    // ========================================================================

    private static async Task DrawCircleAsync(
        ViiperPhysicalMouse mouse,
        int radius,
        int steps,
        int durationMs,
        CancellationToken cancellationToken)
    {
        double stepDelayMs = durationMs / (double)steps;
        double previousX = 0;
        double previousY = 0;

        for (int step = 1; step <= steps; step++)
        {
            double angle = step * (Math.PI * 2.0 / steps);
            double targetX = radius * Math.Cos(angle);
            double targetY = radius * Math.Sin(angle);
            int dx = (int)Math.Round(targetX - previousX);
            int dy = (int)Math.Round(targetY - previousY);

            if (dx != 0 || dy != 0)
            {
                await mouse.SendAsync(new MouseReport(MouseButtons.None, dx, dy, 0), cancellationToken).ConfigureAwait(false);
            }

            previousX += dx;
            previousY += dy;

            if (step < steps)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(stepDelayMs), cancellationToken).ConfigureAwait(false);
            }
        }

        int returnDx = -(int)Math.Round(previousX);
        int returnDy = -(int)Math.Round(previousY);
        if (returnDx != 0 || returnDy != 0)
        {
            await mouse.SendAsync(new MouseReport(MouseButtons.None, returnDx, returnDy, 0), cancellationToken).ConfigureAwait(false);
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
