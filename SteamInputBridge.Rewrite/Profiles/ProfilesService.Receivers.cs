using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace SteamInputBridge.Profiles;

public sealed partial class ProfilesService
{
    private static readonly TimeSpan ReceiverCloseTimeout = TimeSpan.FromSeconds(5);

    // MARK: Receiver Processes
    // ========================================================================

    private static List<int> FindReceivers(IReadOnlyList<string> processNames)
    {
        List<int> processIds = [];
        foreach (string processName in processNames)
        {
            string normalized = Path.GetFileNameWithoutExtension(processName.Trim());
            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            foreach (Process process in Process.GetProcessesByName(normalized))
            {
                try
                {
                    processIds.Add(process.Id);
                }
                catch (InvalidOperationException)
                {
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        return processIds;
    }

    private static int StopReceivers(ResolvedProfile profile)
    {
        int stopped = 0;
        foreach (int processId in FindReceivers(profile.ReceiverProcesses))
        {
            stopped += StopProcess(processId);
        }

        return stopped;
    }

    private static int StopProcess(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            if (process.CloseMainWindow() && process.WaitForExit(ReceiverCloseTimeout))
            {
                return 1;
            }

            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            return 1;
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return 0;
        }
    }
}
