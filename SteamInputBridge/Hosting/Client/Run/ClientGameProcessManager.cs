using System;
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
        if (state.LaunchedProcess is null && !state.KillReceivers)
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

        int killed = state.LaunchedProcess is null
            ? 0
            : GameProcessKiller.KillLaunchedProcess(state.LaunchedProcess);
        if (state.KillReceivers)
        {
            killed += GameProcessKiller.Kill(GameProcessHost.FindReceivers(state.Launch.ReceiverProcesses));
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
