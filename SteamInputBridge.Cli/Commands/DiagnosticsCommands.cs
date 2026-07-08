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
        Command diagnostics = new("diagnostics", "Run app diagnostics.");
        diagnostics.SetAction((_, cancellationToken) => RunAsync(cancellationToken));
        return diagnostics;
    }

    // MARK: Implementation
    // ========================================================================

    private static async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Set up logging
            string path = LogPath(AppContext.BaseDirectory);
            _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using FileStream stream = new(path, FileMode.Create, FileAccess.Write, FileShare.Read, 4096, useAsync: true);
            using StreamWriter writer = new(stream) { AutoFlush = true };

            // Header
            await WriteLineAsync(writer, "Steam Input Bridge diagnostics", cancellationToken).ConfigureAwait(false);
            await WriteLineAsync(writer, $"log=\"{path}\"", cancellationToken).ConfigureAwait(false);

            // Diagnostics
            await WriteLineAsync(writer, "No diagnostics are configured.", cancellationToken).ConfigureAwait(false);

            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return 1;
        }
    }

    private static string LogPath(string baseDirectory)
    {
        string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string processId = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
        return Path.Combine(baseDirectory, "logs", $"diagnostics-{timestamp}-{processId}.log");
    }

    private static async Task WriteLineAsync(
        TextWriter writer,
        string line,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Console.Out.WriteLineAsync(line).ConfigureAwait(false);
        await writer.WriteLineAsync(line).ConfigureAwait(false);
    }
}
