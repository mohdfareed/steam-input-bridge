using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace SteamInputBridge.App.Cli;

internal static class ClientServerBootstrapper
{
    private const string PipeName = "SteamInputBridge";
    private const string InstanceSemaphoreName = @"Local\SteamInputBridge.Server";
    private static readonly TimeSpan ServerStartupTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PipeProbeTimeout = TimeSpan.FromMilliseconds(250);

    public static async Task EnsureServerStartedAsync(CancellationToken cancellationToken)
    {
        if (!IsServerOwned())
        {
            StartTrayServer();
        }

        await WaitForServerPipeAsync(cancellationToken).ConfigureAwait(false);
    }

    private static bool IsServerOwned()
    {
        try
        {
            using Semaphore semaphore = Semaphore.OpenExisting(InstanceSemaphoreName);
            if (!semaphore.WaitOne(0))
            {
                return true;
            }

            _ = semaphore.Release();
            return false;
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            // If Windows will not let us inspect the semaphore, avoid starting
            // a second server and let the pipe probe decide whether it is alive.
            return true;
        }
    }

    private static void StartTrayServer()
    {
        if (Environment.ProcessPath is not { Length: > 0 } processPath)
        {
            throw new InvalidOperationException("Cannot start the server because the app path is unknown.");
        }

        if (TryStartTrayServerThroughDesktopShell(processPath))
        {
            return;
        }

        _ = Process.Start(new ProcessStartInfo
        {
            FileName = processPath,
            Arguments = "tray",
            WorkingDirectory = System.AppContext.BaseDirectory,
            // Steam can keep a shortcut "running" while direct child
            // processes stay alive. Shell execution makes the autostarted
            // tray behave like a normal double-click/startup launch instead
            // of a child helper owned by the client run.
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        });
    }

    private static bool TryStartTrayServerThroughDesktopShell(string processPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        Type? shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType is null)
        {
            return false;
        }

        object? shell = null;
        try
        {
            shell = Activator.CreateInstance(shellType);
            if (shell is null)
            {
                return false;
            }

            // Steam-launched clients carry Steam-specific SDL environment.
            // Ask Explorer's shell COM server to launch the tray app so the
            // server starts like a desktop/startup launch instead of as a
            // child of the Steam shortcut process.
            _ = shellType.InvokeMember(
                "ShellExecute",
                BindingFlags.InvokeMethod,
                binder: null,
                target: shell,
                args:
                [
                    processPath,
                    "tray",
                    System.AppContext.BaseDirectory,
                    "open",
                    0,
                ],
                culture: CultureInfo.InvariantCulture);
            return true;
        }
        catch (COMException)
        {
            return false;
        }
        catch (TargetInvocationException)
        {
            return false;
        }
        finally
        {
            if (shell is not null && Marshal.IsComObject(shell))
            {
                _ = Marshal.FinalReleaseComObject(shell);
            }
        }
    }

    private static async Task WaitForServerPipeAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ServerStartupTimeout);

        while (!timeout.IsCancellationRequested)
        {
            try
            {
                using NamedPipeClientStream pipe = new(
                    ".",
                    PipeName,
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous);
                await pipe
                    .ConnectAsync((int)PipeProbeTimeout.TotalMilliseconds, timeout.Token)
                    .ConfigureAwait(false);
                return;
            }
            catch (TimeoutException)
            {
            }
            catch (IOException)
            {
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                break;
            }

            await Task.Delay(100, timeout.Token).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        throw new TimeoutException("The Steam Input Bridge server did not become available.");
    }
}
