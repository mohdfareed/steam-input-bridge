using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;

const int TimestampBytes = sizeof(long);
const int SequenceBytes = sizeof(int);

if (args.Length > 0 && args[0] == "--reader")
{
    RunReader(args);
    return;
}

BenchOptions options = BenchOptions.Parse(args);
List<BenchResult> results = [];
foreach (FrameSpec frame in FrameSpec.All)
{
    foreach (Direction direction in Enum.GetValues<Direction>())
    {
        results.Add(RunParent(frame, direction, rateHz: 0, options));
        foreach (int rateHz in options.RatesHz)
        {
            results.Add(RunParent(frame, direction, rateHz, options));
        }
    }
}

Console.WriteLine(
    "frame  direction      rate_hz  payload  reports  send_rate/s  avg_us  p50_us  p95_us  p99_us  p999_us  max_us  >100us  >250us  >500us  >1ms  drops  reader_alloc_B_frame");
foreach (BenchResult result in results)
{
    Console.WriteLine(
        string.Create(
            CultureInfo.InvariantCulture,
            $"{result.Frame.Name,-5}  {result.Direction,-13}  {result.RateHz,7}  {result.Frame.PayloadBytes,7}  {result.Count,7}  " +
            $"{result.SendRatePerSecond,11:0}  {result.AvgMicroseconds,6:0.000}  {result.P50Microseconds,6:0.000}  " +
            $"{result.P95Microseconds,6:0.000}  {result.P99Microseconds,6:0.000}  {result.P999Microseconds,7:0.000}  " +
            $"{result.MaxMicroseconds,6:0.000}  {result.SpikesOver100Microseconds,6}  {result.SpikesOver250Microseconds,6}  " +
            $"{result.SpikesOver500Microseconds,6}  {result.SpikesOver1000Microseconds,4}  {result.Drops,5}  " +
            $"{result.ReaderAllocatedBytesPerFrame,20:0.000}"));
}

static BenchResult RunParent(FrameSpec frame, Direction direction, int rateHz, BenchOptions options)
{
    string pipeName = $"vm-hotpath-{Guid.NewGuid():N}";
    int total = options.Warmup + options.Count;
    string assemblyPath = Environment.ProcessPath ??
        throw new InvalidOperationException("Could not resolve benchmark executable path.");

    using Process reader = StartReader(
        assemblyPath,
        pipeName,
        direction,
        frame.PayloadBytes,
        options.Count,
        options.Warmup);

    using Stream writerStream = OpenWriter(pipeName, direction);
    byte[] frameBuffer = new byte[TimestampBytes + SequenceBytes + frame.PayloadBytes];
    long periodTicks = rateHz == 0 ? 0 : Stopwatch.Frequency / rateHz;
    long nextTick = Stopwatch.GetTimestamp();
    long startedTicks = Stopwatch.GetTimestamp();

    for (int i = 0; i < total; i++)
    {
        if (rateHz > 0)
        {
            WaitUntil(nextTick);
            nextTick += periodTicks;
        }

        BinaryPrimitives.WriteInt64LittleEndian(frameBuffer, Stopwatch.GetTimestamp());
        BinaryPrimitives.WriteInt32LittleEndian(frameBuffer.AsSpan(TimestampBytes), i);
        FillPayload(frameBuffer.AsSpan(TimestampBytes + SequenceBytes), frame.Name, i);
        writerStream.Write(frameBuffer);
        writerStream.Flush();
    }

    long finishedTicks = Stopwatch.GetTimestamp();
    writerStream.Dispose();
    string output = reader.StandardOutput.ReadToEnd();
    string error = reader.StandardError.ReadToEnd();
    if (!reader.WaitForExit(10_000) || reader.ExitCode != 0)
    {
        throw new InvalidOperationException($"Reader failed. exit={reader.ExitCode} stdout={output} stderr={error}");
    }

    ReaderResult readerResult = ReaderResult.Parse(output);
    double elapsedSeconds = (finishedTicks - startedTicks) / (double)Stopwatch.Frequency;
    return new BenchResult(
        frame,
        direction,
        rateHz,
        options.Count,
        total / elapsedSeconds,
        readerResult.AvgMicroseconds,
        readerResult.P50Microseconds,
        readerResult.P95Microseconds,
        readerResult.P99Microseconds,
        readerResult.P999Microseconds,
        readerResult.MaxMicroseconds,
        readerResult.SpikesOver100Microseconds,
        readerResult.SpikesOver250Microseconds,
        readerResult.SpikesOver500Microseconds,
        readerResult.SpikesOver1000Microseconds,
        readerResult.Drops,
        readerResult.AllocatedBytesPerFrame);
}

static Process StartReader(
    string assemblyPath,
    string pipeName,
    Direction direction,
    int payloadBytes,
    int count,
    int warmup)
{
    ProcessStartInfo startInfo = new()
    {
        FileName = assemblyPath,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    };
    startInfo.ArgumentList.Add("--reader");
    startInfo.ArgumentList.Add(pipeName);
    startInfo.ArgumentList.Add(direction.ToString());
    startInfo.ArgumentList.Add(payloadBytes.ToString(CultureInfo.InvariantCulture));
    startInfo.ArgumentList.Add(count.ToString(CultureInfo.InvariantCulture));
    startInfo.ArgumentList.Add(warmup.ToString(CultureInfo.InvariantCulture));

    return Process.Start(startInfo) ??
        throw new InvalidOperationException("Could not start benchmark reader process.");
}

static Stream OpenWriter(string pipeName, Direction direction)
{
    if (direction == Direction.ServerToClient)
    {
        NamedPipeServerStream server = new(
            pipeName,
            PipeDirection.Out,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.None);
        server.WaitForConnection();
        return server;
    }

    NamedPipeClientStream client = new(".", pipeName, PipeDirection.Out, PipeOptions.None);
    client.Connect(10_000);
    return client;
}

static void RunReader(string[] args)
{
    if (args.Length != 6)
    {
        throw new ArgumentException("Reader args: --reader <pipe> <direction> <payloadBytes> <count> <warmup>");
    }

    string pipeName = args[1];
    Direction direction = Enum.Parse<Direction>(args[2]);
    int payloadBytes = int.Parse(args[3], CultureInfo.InvariantCulture);
    int count = int.Parse(args[4], CultureInfo.InvariantCulture);
    int warmup = int.Parse(args[5], CultureInfo.InvariantCulture);
    int total = count + warmup;

    using Stream readerStream = OpenReader(pipeName, direction);
    byte[] frameBuffer = new byte[TimestampBytes + SequenceBytes + payloadBytes];
    long[] latencies = new long[count];
    int drops = 0;
    int previousSequence = -1;

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();

    for (int i = 0; i < total; i++)
    {
        ReadExactly(readerStream, frameBuffer);
        long sentTicks = BinaryPrimitives.ReadInt64LittleEndian(frameBuffer);
        int sequence = BinaryPrimitives.ReadInt32LittleEndian(frameBuffer.AsSpan(TimestampBytes));

        if (previousSequence >= 0 && sequence != previousSequence + 1)
        {
            drops += sequence - previousSequence - 1;
        }

        previousSequence = sequence;
        if (i >= warmup)
        {
            latencies[i - warmup] = Stopwatch.GetTimestamp() - sentTicks;
        }
    }

    long allocatedAfter = GC.GetAllocatedBytesForCurrentThread();
    Array.Sort(latencies);
    double tickMicroseconds = 1_000_000.0 / Stopwatch.Frequency;
    double averageTicks = 0;
    foreach (long latency in latencies)
    {
        averageTicks += latency;
    }

    averageTicks /= latencies.Length;
    ReaderResult result = new(
        averageTicks * tickMicroseconds,
        Percentile(latencies, 0.50) * tickMicroseconds,
        Percentile(latencies, 0.95) * tickMicroseconds,
        Percentile(latencies, 0.99) * tickMicroseconds,
        Percentile(latencies, 0.999) * tickMicroseconds,
        latencies[^1] * tickMicroseconds,
        CountOver(latencies, MicrosecondsToTicks(100)),
        CountOver(latencies, MicrosecondsToTicks(250)),
        CountOver(latencies, MicrosecondsToTicks(500)),
        CountOver(latencies, MicrosecondsToTicks(1000)),
        drops,
        (allocatedAfter - allocatedBefore) / (double)total);

    Console.WriteLine(result.Format());
}

static Stream OpenReader(string pipeName, Direction direction)
{
    if (direction == Direction.ServerToClient)
    {
        NamedPipeClientStream client = new(".", pipeName, PipeDirection.In, PipeOptions.None);
        client.Connect(10_000);
        return client;
    }

    NamedPipeServerStream server = new(
        pipeName,
        PipeDirection.In,
        maxNumberOfServerInstances: 1,
        PipeTransmissionMode.Byte,
        PipeOptions.None);
    server.WaitForConnection();
    return server;
}

static void ReadExactly(Stream stream, byte[] buffer)
{
    int read = 0;
    while (read < buffer.Length)
    {
        int count = stream.Read(buffer, read, buffer.Length - read);
        if (count == 0)
        {
            throw new EndOfStreamException();
        }

        read += count;
    }
}

static void FillPayload(Span<byte> payload, string frame, int sequence)
{
    payload.Clear();
    BinaryPrimitives.WriteInt32LittleEndian(payload, sequence);
    if (frame == "gyro")
    {
        for (int i = 0; i < 6; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(payload[(sizeof(int) + (i * sizeof(float)))..], sequence + i);
        }
    }
    else
    {
        BinaryPrimitives.WriteInt32LittleEndian(payload[sizeof(int)..], sequence);
        BinaryPrimitives.WriteInt32LittleEndian(payload[(sizeof(int) * 2)..], -sequence);
        BinaryPrimitives.WriteInt32LittleEndian(payload[(sizeof(int) * 3)..], sequence & 120);
    }
}

static void WaitUntil(long targetTick)
{
    while (Stopwatch.GetTimestamp() < targetTick)
    {
    }
}

static long Percentile(long[] sorted, double percentile)
{
    int index = Math.Min(sorted.Length - 1, (int)(sorted.Length * percentile));
    return sorted[index];
}

static int CountOver(long[] sorted, long thresholdTicks)
{
    int index = Array.BinarySearch(sorted, thresholdTicks + 1);
    if (index < 0)
    {
        index = ~index;
    }

    return sorted.Length - index;
}

static long MicrosecondsToTicks(int microseconds)
{
    return (long)(microseconds * (Stopwatch.Frequency / 1_000_000.0));
}

internal enum Direction
{
    ServerToClient,
    ClientToServer,
}

internal readonly record struct FrameSpec(string Name, int PayloadBytes)
{
    public static IReadOnlyList<FrameSpec> All { get; } =
    [
        new("gyro", sizeof(int) + (6 * sizeof(float))),
        new("mouse", sizeof(int) + (3 * sizeof(int))),
    ];
}

internal sealed record BenchOptions(int Count, int Warmup, IReadOnlyList<int> RatesHz)
{
    public static BenchOptions Parse(string[] args)
    {
        int count = 20_000;
        int warmup = 1_000;
        int[] rates = [1000];
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--count" && i + 1 < args.Length)
            {
                count = int.Parse(args[++i], CultureInfo.InvariantCulture);
            }
            else if (args[i] == "--warmup" && i + 1 < args.Length)
            {
                warmup = int.Parse(args[++i], CultureInfo.InvariantCulture);
            }
            else if (args[i] == "--rates" && i + 1 < args.Length)
            {
                rates = [.. args[++i]
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(rate => int.Parse(rate, CultureInfo.InvariantCulture))];
            }
        }

        return new BenchOptions(count, warmup, rates);
    }
}

internal readonly record struct ReaderResult(
    double AvgMicroseconds,
    double P50Microseconds,
    double P95Microseconds,
    double P99Microseconds,
    double P999Microseconds,
    double MaxMicroseconds,
    int SpikesOver100Microseconds,
    int SpikesOver250Microseconds,
    int SpikesOver500Microseconds,
    int SpikesOver1000Microseconds,
    int Drops,
    double AllocatedBytesPerFrame)
{
    public string Format()
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{AvgMicroseconds},{P50Microseconds},{P95Microseconds},{P99Microseconds},{P999Microseconds}," +
            $"{MaxMicroseconds},{SpikesOver100Microseconds},{SpikesOver250Microseconds}," +
            $"{SpikesOver500Microseconds},{SpikesOver1000Microseconds},{Drops},{AllocatedBytesPerFrame}");
    }

    public static ReaderResult Parse(string value)
    {
        string[] parts = value.Trim().Split(',');
        return new ReaderResult(
            double.Parse(parts[0], CultureInfo.InvariantCulture),
            double.Parse(parts[1], CultureInfo.InvariantCulture),
            double.Parse(parts[2], CultureInfo.InvariantCulture),
            double.Parse(parts[3], CultureInfo.InvariantCulture),
            double.Parse(parts[4], CultureInfo.InvariantCulture),
            double.Parse(parts[5], CultureInfo.InvariantCulture),
            int.Parse(parts[6], CultureInfo.InvariantCulture),
            int.Parse(parts[7], CultureInfo.InvariantCulture),
            int.Parse(parts[8], CultureInfo.InvariantCulture),
            int.Parse(parts[9], CultureInfo.InvariantCulture),
            int.Parse(parts[10], CultureInfo.InvariantCulture),
            double.Parse(parts[11], CultureInfo.InvariantCulture));
    }
}

internal readonly record struct BenchResult(
    FrameSpec Frame,
    Direction Direction,
    int RateHz,
    int Count,
    double SendRatePerSecond,
    double AvgMicroseconds,
    double P50Microseconds,
    double P95Microseconds,
    double P99Microseconds,
    double P999Microseconds,
    double MaxMicroseconds,
    int SpikesOver100Microseconds,
    int SpikesOver250Microseconds,
    int SpikesOver500Microseconds,
    int SpikesOver1000Microseconds,
    int Drops,
    double ReaderAllocatedBytesPerFrame);
