using System;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using SteamInputBridge.App.Hosting;
using SteamInputBridge.Hosting;

namespace SteamInputBridge.App.Cli;

internal static class ShortcutCommands
{
    private static readonly TimeSpan ServerStartupTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PipeProbeTimeout = TimeSpan.FromMilliseconds(250);

    public static Command CreateCommand()
    {
        Command shortcut = new("shortcut", "Run from a Steam shortcut.");
        shortcut.Arguments.Add(new Argument<string>("profile") { Description = "Profile id to run." });
        shortcut.SetAction(async (parseResult, cancellationToken) =>
        {
            string profileId = parseResult.GetValue<string>("profile")!;
            await EnsureServerStartedAsync(cancellationToken).ConfigureAwait(false);
            using IHost host = AppHost.CreateClient(profileId);
            await host.RunAsync(cancellationToken).ConfigureAwait(false);
        });
        return shortcut;
    }

    private static async Task EnsureServerStartedAsync(CancellationToken cancellationToken)
    {
        if (!await IsServerRunningAsync(cancellationToken).ConfigureAwait(false))
        {
            StartTray();
        }

        await WaitForServerAsync(cancellationToken).ConfigureAwait(false);
    }

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

    private static void StartTray()
    {
        string processPath = ResolveProcessPath();
        ProcessStartInfo start = new()
        {
            FileName = processPath,
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        start.ArgumentList.Add("tray");

        using Process? process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the tray server.");
    }

    private static async Task WaitForServerAsync(CancellationToken cancellationToken)
    {
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
        throw new TimeoutException("The Steam Input Bridge server did not become available.");
    }

    private static string ResolveProcessPath()
    {
        return Environment.ProcessPath is { Length: > 0 } processPath
            ? processPath
            : throw new InvalidOperationException("Cannot start Steam Input Bridge because the app path is unknown.");
    }
}
