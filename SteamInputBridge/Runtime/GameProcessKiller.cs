using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace SteamInputBridge.Runtime;

/// <summary>Stops processes explicitly owned or selected by the caller.</summary>
public static class GameProcessKiller
{
    /// <summary>Stops the launched root process tree.</summary>
    public static int KillLaunchedProcess(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        return KillRoot(process);
    }

    /// <summary>Stops one process by id.</summary>
    public static int KillProcess(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            if (!process.HasExited)
            {
                process.Kill();
                return 1;
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException or
                InvalidOperationException or
                NotSupportedException or
                System.ComponentModel.Win32Exception)
        {
        }

        return 0;
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

        int killed = Kill(GameProcessHost.FindReceivers(receiverProcesses));
        killed += KillRoot(root);
        return killed;
    }

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
}
