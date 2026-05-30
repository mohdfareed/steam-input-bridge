using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Vanara.PInvoke;
using static Vanara.PInvoke.Shell32;

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

        IntPtr result = ShellExecute(
            default,
            "open",
            processPath,
            "tray",
            System.AppContext.BaseDirectory,
            ShowWindowCommand.SW_HIDE);
        if (result.ToInt64() <= 32)
        {
            throw new InvalidOperationException("Could not start the tray server.");
        }
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

        IShellDispatch2? shell = null;
        try
        {
            shell = Activator.CreateInstance(shellType) as IShellDispatch2;
            if (shell is null)
            {
                return false;
            }

            // Steam-launched clients carry Steam-specific SDL environment.
            // Ask Explorer's shell COM server to launch the tray app so the
            // server starts like a desktop/startup launch instead of as a
            // child of the Steam shortcut process.
            shell.ShellExecute(
                processPath,
                "tray",
                System.AppContext.BaseDirectory,
                "open",
                0);
            return true;
        }
        catch (Exception exception) when (exception is COMException or InvalidCastException)
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
