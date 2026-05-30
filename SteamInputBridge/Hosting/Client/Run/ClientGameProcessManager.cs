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
        if (string.IsNullOrWhiteSpace(state.Launch.Executable))
        {
            return;
        }

        // Receiver processes that already existed before this launch are not
        // ours. Track only post-launch receiver pids so client shutdown can
        // close launcher-escaped games without killing unrelated matches.
        state.CaptureReceiverBaseline(GameProcessHost.FindReceivers(state.Launch.ReceiverProcesses));
        Process process = GameProcessHost.Launch(
            state.Launch.Executable,
            state.Launch.Arguments,
            state.Launch.WorkingDirectory ?? AppContext.BaseDirectory);
        state.LaunchedProcess = process;
        state.ProcessOwner = TryOwnProcessTree(process);
    }

    public void LogStarted(ClientRunState state)
    {
        if (state.LaunchedProcess is null)
        {
            HostingLog.Attached(logger, state.Launch.ProfileId);
            return;
        }

        HostingLog.Started(logger, state.Launch.ProfileId, state.LaunchedProcess.Id);
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

        IReadOnlyList<ObservedGameProcess> currentReceivers =
            GameProcessHost.FindReceivers(state.Launch.ReceiverProcesses);
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
