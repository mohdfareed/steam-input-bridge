using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using SteamInputBridge.App.Host;
using SteamInputBridge.Hosting;

namespace SteamInputBridge.App.Shortcut;

internal static class ShortcutMode
{
    private static readonly TimeSpan ServerStartupTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PipeProbeTimeout = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan PipeProbeDelay = TimeSpan.FromMilliseconds(100);

    // MARK: Publics
    // ========================================================================

    public static async Task<int> RunAsync(string profileId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        if (!await IsServerRunningAsync(cancellationToken).ConfigureAwait(false))
        {
            await StartTrayServer(cancellationToken).ConfigureAwait(false);
        }

        using IHost host = AppHost.CreateClient(profileId);
        await host.RunAsync(cancellationToken).ConfigureAwait(false);
        return 0;
    }

    // MARK: Implementation
    // ========================================================================

    private static async Task<bool> IsServerRunningAsync(CancellationToken cancellationToken)
    {
        try
        {
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
        string processPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot start the server because the app path is unknown.");

        ProcessStartInfo start = new()
        {
            FileName = "explorer.exe",
            UseShellExecute = false,
        };

        // Start tray server through explorer to prevent server shutdown on client shutdown.
        start.ArgumentList.Add(processPath);
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the tray server.");

        // Wait for the server to start by probing the control API pipe.
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ServerStartupTimeout);
        while (!timeout.IsCancellationRequested)
        {
            if (await IsServerRunningAsync(timeout.Token).ConfigureAwait(false))
            {
                return;
            }

            await Task.Delay(PipeProbeDelay, timeout.Token).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        throw new TimeoutException("The Steam Input Bridge server did not start.");
    }
}
