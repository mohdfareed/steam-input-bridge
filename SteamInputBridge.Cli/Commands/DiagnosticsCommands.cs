using System;
using System.CommandLine;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SteamInputBridge.Cli.Commands;

internal static class DiagnosticsCommands
{
    public static Command CreateCommand()
    {
        Command diagnostics = new("diagnostics", "App diagnostics.");
        diagnostics.SetAction((_, cancellationToken) => RunAsync(cancellationToken));
        return diagnostics;
    }

    // MARK: Implementation
    // ========================================================================

    private static async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            using DiagnosticTranscript transcript = DiagnosticTranscript.Create(AppContext.BaseDirectory);
            await transcript.WriteLineAsync("Starting diagnostics").ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }

        return 0;
    }

    // MARK: Transcript
    // ========================================================================

    private sealed class DiagnosticTranscript : IDisposable
    {
        private readonly StreamWriter _writer;

        private DiagnosticTranscript(string path)
        {
            Path = path;
            _ = Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            _writer = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                AutoFlush = true,
            };
        }

        public string Path { get; }

        public static DiagnosticTranscript Create(string baseDirectory)
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string fileName = $"diagnostics-2xko-steam-controllers-{timestamp}-{Environment.ProcessId.ToString(CultureInfo.InvariantCulture)}.log";
            return new DiagnosticTranscript(System.IO.Path.Combine(baseDirectory, "logs", fileName));
        }

        public void WriteLine(string line)
        {
            Console.WriteLine(line);
            _writer.WriteLine(line);
        }

        public async Task WriteLineAsync(string line)
        {
            await Console.Out.WriteLineAsync(line).ConfigureAwait(false);
            await _writer.WriteLineAsync(line).ConfigureAwait(false);
        }

        public void Dispose()
        {
            _writer.Dispose();
        }
    }
}
