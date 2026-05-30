using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Runtime;

namespace SteamInputBridge.Hosting.Client.Run;

internal sealed class ClientGameProcessManager(ILogger logger)
{
    public void StartProfileProcess(ClientRunState state)
    {
        ClientRunLaunch launch = state.RegisteredLaunch;
        if (string.IsNullOrWhiteSpace(launch.Executable))
        {
            return;
        }

        // Receiver processes that already existed before this launch are not
        // ours. Track only post-launch receiver pids so client shutdown can
        // close launcher-escaped games without killing unrelated matches.
        state.CaptureReceiverBaseline(GameProcessHost.FindReceivers(launch.ReceiverProcesses));
        Process process = GameProcessHost.Launch(
            launch.Executable,
            launch.Arguments,
            launch.WorkingDirectory ?? AppContext.BaseDirectory);
        state.LaunchedProcess = process;
        state.ProcessOwner = TryOwnProcessTree(process);
    }

    public void LogStarted(ClientRunState state)
    {
        ClientRunLaunch launch = state.RegisteredLaunch;
        if (state.LaunchedProcess is null)
        {
            HostingLog.Attached(logger, launch.ProfileId);
            return;
        }

        HostingLog.Started(logger, launch.ProfileId, state.LaunchedProcess.Id);
    }

    public void StopGameProcesses(ClientRunState state, string reason)
    {
        if (state.LaunchedProcess is null)
        {
            return;
        }

        lock (state.StopGate)
        {
            if (state.GameStopRequested)
            {
                return;
            }

            state.GameStopRequested = true;
        }

        ClientRunLaunch launch = state.RegisteredLaunch;
        IReadOnlyList<ObservedGameProcess> currentReceivers =
            GameProcessHost.FindReceivers(launch.ReceiverProcesses);
        IReadOnlyList<ObservedGameProcess> ownedReceivers =
            state.GetOwnedReceiversSnapshot(currentReceivers);
        int killed = GameProcessKiller.Kill(ownedReceivers);

        if (state.LaunchedProcess is not null)
        {
            killed += GameProcessKiller.Kill(state.LaunchedProcess);
        }

        HostingLog.StoppedGameProcesses(logger, reason, killed);
    }

    private IDisposable? TryOwnProcessTree(Process process)
    {
        try
        {
            return GameProcessHost.OwnProcessTree(process);
        }
        catch (Exception exception) when (
            exception is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            HostingLog.CouldNotAttachProcessJob(logger, exception.Message);
            return null;
        }
    }
}
