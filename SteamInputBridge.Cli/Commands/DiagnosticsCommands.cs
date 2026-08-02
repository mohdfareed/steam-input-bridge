using System;
using System.CommandLine;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SteamInputBridge.Cli.Host;
using SteamInputBridge.Diagnostics;
using SteamInputBridge.Settings;

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
            using IHost host = CliHost.CreateCli();
            SettingsFile settingsFile = host.Services.GetRequiredService<SettingsFile>();
            IConfiguration configuration = host.Services.GetRequiredService<IConfiguration>();
            LoggingSettings settings = new();
            configuration.GetSection(LoggingSettings.SectionName).Bind(settings);

            string path = FileLoggerProvider.CreateLogPath(
                settingsFile,
                prefix: "diagnostics-",
                logDirectory: settings.LogDirectory);
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
