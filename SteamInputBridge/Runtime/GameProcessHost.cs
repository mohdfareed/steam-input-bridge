using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace SteamInputBridge.Runtime;

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

    /// <summary>Gets a process executable path when the platform exposes it.</summary>
    public static string? GetExecutablePath(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return process.MainModule?.FileName;
        }
        catch (Exception exception) when (
            exception is ArgumentException or
                InvalidOperationException or
                NotSupportedException or
                System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
