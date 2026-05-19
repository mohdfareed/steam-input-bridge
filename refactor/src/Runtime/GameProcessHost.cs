using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace VirtualMouse.Runtime;

/// <summary>Launches, observes, and stops game processes.</summary>
public static class GameProcessHost
{
    // MARK: Management
    // ========================================================================

    /// <summary>Starts a process using the resolved profile launch details.</summary>
    public static Process Launch(
        string executable,
        string arguments,
        string workingDirectory)
    {
        return Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
        }) ?? throw new InvalidOperationException($"Could not launch {executable}.");
    }

    /// <summary>Owns a process tree when the platform supports it.</summary>
    public static IDisposable OwnProcessTree(Process process)
    {
        return OperatingSystem.IsWindows()
            ? WindowsProcessJob.Own(process)
            : new NoopDisposable();
    }

    /// <summary>Finds receiver processes by executable name.</summary>
    public static IReadOnlyList<ObservedGameProcess> FindReceivers(IReadOnlyList<string> processNames)
    {
        ArgumentNullException.ThrowIfNull(processNames);

        Dictionary<int, ObservedGameProcess> processes = [];
        foreach (string processName in processNames)
        {
            if (string.IsNullOrWhiteSpace(processName))
            {
                continue;
            }

            foreach (Process process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(processName)))
            {
                try
                {
                    processes[process.Id] = new ObservedGameProcess(
                        process.Id,
                        Path.GetFileName(processName.Trim()));
                }
                catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
                {
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        return [.. processes.Values];
    }

    // MARK: Ending
    // ========================================================================

    /// <summary>Stops the launched root process tree.</summary>
    public static int KillLaunchedProcess(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        return KillRoot(process);
    }

    /// <summary>Stops the listed processes and returns how many kill requests were sent.</summary>
    public static int Kill(IReadOnlyList<ObservedGameProcess> processes)
    {
        ArgumentNullException.ThrowIfNull(processes);

        int killed = 0;
        foreach (ObservedGameProcess observed in processes)
        {
            try
            {
                using Process process = Process.GetProcessById(observed.ProcessId);
                if (!process.HasExited)
                {
                    process.Kill();
                    killed++;
                }
            }
            catch (Exception exception) when (
                exception is ArgumentException or
                    InvalidOperationException or
                    NotSupportedException or
                    System.ComponentModel.Win32Exception)
            {
            }
        }

        return killed;
    }

    /// <summary>Stops the root process and known receiver processes.</summary>
    public static int KillRootAndReceivers(Process root, IReadOnlyList<ObservedGameProcess> receivers)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(receivers);

        int killed = Kill(receivers);
        killed += KillRoot(root);
        return killed;
    }

    /// <summary>Finds receiver processes, then stops the receivers and root process.</summary>
    public static int KillRootAndReceivers(Process root, IReadOnlyList<string> receiverProcesses)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(receiverProcesses);

        int killed = Kill(FindReceivers(receiverProcesses));
        killed += KillRoot(root);
        return killed;
    }

    // MARK: Privates
    // ========================================================================

    private static int KillRoot(Process root)
    {
        try
        {
            if (!root.HasExited)
            {
                root.Kill(entireProcessTree: true);
                return 1;
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
                NotSupportedException or
                System.ComponentModel.Win32Exception)
        {
        }

        return 0;
    }

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
