using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using VirtualMouse.Runtime;

namespace VirtualMouse.Hosting;

internal static class GameProcessHost
{
    public static Process Launch(ClientRunLaunch run)
    {
        return Process.Start(new ProcessStartInfo
        {
            FileName = run.Executable,
            Arguments = run.Arguments,
            WorkingDirectory = run.WorkingDirectory,
            UseShellExecute = false,
        }) ?? throw new InvalidOperationException($"Could not launch {run.Executable}.");
    }

    public static IReadOnlyList<ObservedGameProcess> FindReceivers(IReadOnlyList<string> processNames)
    {
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

    public static int Kill(IReadOnlyList<ObservedGameProcess> processes)
    {
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
}
