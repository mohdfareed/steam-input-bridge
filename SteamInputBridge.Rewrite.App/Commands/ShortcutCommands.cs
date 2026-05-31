using System;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using SteamInputBridge.Hosting;

namespace SteamInputBridge.App.Commands;

internal static class ShortcutCommands
{
    private static readonly TimeSpan ServerStartupTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PipeProbeTimeout = TimeSpan.FromMilliseconds(250);

    public static Command CreateCommand()
    {
        Command shortcut = new("shortcut", "Run from a Steam shortcut.");
        shortcut.Arguments.Add(new Argument<string>("profile") { Description = "Profile id to run." });
        shortcut.SetAction(async (parseResult, cancellationToken) =>
        {
            string profileId = parseResult.GetValue<string>("profile")!;
            await StartClient(profileId, cancellationToken).ConfigureAwait(false);
        });
        return shortcut;
    }

    private static async Task StartClient(string profileId, CancellationToken cancellationToken)
    {
        if (!await IsServerRunningAsync(cancellationToken).ConfigureAwait(false))
        {
            await StartTrayServer(cancellationToken).ConfigureAwait(false);
        }

        using IHost host = AppHost.CreateClient(profileId);
        await host.RunAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> IsServerRunningAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Connect to the server's control pipe to check if it's running.
            using NamedPipeClientStream pipe = new(".", IBridgeControlApi.Name, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync((int)PipeProbeTimeout.TotalMilliseconds, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (exception is IOException or TimeoutException)
        {
            return false;
        }
    }

    private static async Task StartTrayServer(CancellationToken cancellationToken)
    {
        ProcessStartInfo start = new()
        {
            FileName = Environment.ProcessPath ?? throw new InvalidOperationException("Application process path is not available."),
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        // Start the server in tray mode.
        start.ArgumentList.Add("tray");
        using Process? process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the tray server.");

        // Wait for the server to start and become available.
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ServerStartupTimeout);
        while (!timeout.IsCancellationRequested)
        {
            if (await IsServerRunningAsync(timeout.Token).ConfigureAwait(false))
            {
                return;
            }
            await Task.Delay(100, timeout.Token).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        throw new TimeoutException("The Steam Input Bridge server did not start.");
    }
}
