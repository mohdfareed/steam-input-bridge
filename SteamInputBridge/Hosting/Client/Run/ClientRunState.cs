using System;
using System.Collections.Generic;
using System.Diagnostics;
using SteamInputBridge.Hosting.Client.Run.Controllers;
using SteamInputBridge.Runtime;

namespace SteamInputBridge.Hosting.Client.Run;

internal sealed class ClientRunState(
    ClientRunLaunch launch,
    Guid? registeredClientId,
    StartRunRequest request)
{
    public ClientRunLaunch Launch { get; set; } = launch;

    public Process? LaunchedProcess { get; set; }

    public IDisposable? ProcessOwner { get; set; }

    public Guid? RegisteredClientId { get; set; } = registeredClientId;

    public StartRunRequest Request { get; } = request;

    public bool SawReceiver { get; set; }

    public IClientControllerStreams? ControllerStreams { get; set; }

    public string? LastObservedSignature { get; set; }

    public object StopGate { get; } = new();

    public bool GameStopRequested { get; set; }

    private HashSet<int> ReceiverBaselineProcessIds { get; } = [];

    private Dictionary<int, ObservedGameProcess> OwnedReceivers { get; } = [];

    public void CaptureReceiverBaseline(IReadOnlyList<ObservedGameProcess> receivers)
    {
        lock (StopGate)
        {
            ReceiverBaselineProcessIds.Clear();
            foreach (ObservedGameProcess receiver in receivers)
            {
                _ = ReceiverBaselineProcessIds.Add(receiver.ProcessId);
            }
        }
    }

    public IReadOnlyList<ObservedGameProcess> UpdateReceivers(IReadOnlyList<ObservedGameProcess> receivers)
    {
        if (LaunchedProcess is null)
        {
            return receivers;
        }

        lock (StopGate)
        {
            List<ObservedGameProcess> current = [];
            foreach (ObservedGameProcess receiver in receivers)
            {
                if (ReceiverBaselineProcessIds.Contains(receiver.ProcessId))
                {
                    continue;
                }

                current.Add(receiver);
                OwnedReceivers[receiver.ProcessId] = receiver;
            }

            return current;
        }
    }

    public IReadOnlyList<ObservedGameProcess> GetOwnedReceiversSnapshot(
        IReadOnlyList<ObservedGameProcess> currentReceivers)
    {
        ArgumentNullException.ThrowIfNull(currentReceivers);

        if (LaunchedProcess is null)
        {
            return [];
        }

        lock (StopGate)
        {
            // Receiver monitors can miss the final handoff from a launcher.
            // At shutdown, own any currently-running receiver that appeared
            // after our pre-launch baseline.
            foreach (ObservedGameProcess receiver in currentReceivers)
            {
                if (!ReceiverBaselineProcessIds.Contains(receiver.ProcessId))
                {
                    OwnedReceivers[receiver.ProcessId] = receiver;
                }
            }

            return [.. OwnedReceivers.Values];
        }
    }
}
