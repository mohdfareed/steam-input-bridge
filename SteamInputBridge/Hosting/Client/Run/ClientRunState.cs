using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using SteamInputBridge.Hosting.Client.Run.Controllers;
using SteamInputBridge.Runtime;

namespace SteamInputBridge.Hosting.Client.Run;

internal sealed class ClientRunState(
    ClientRunLaunch launch,
    Guid? registeredClientId,
    StartRunRequest request,
    bool killReceivers)
{
    public ClientRunLaunch Launch { get; set; } = launch;

    public Process? LaunchedProcess { get; set; }

    public IDisposable? ProcessOwner { get; set; }

    public Guid? RegisteredClientId { get; set; } = registeredClientId;

    public StartRunRequest Request { get; } = request;

    public bool KillReceivers { get; } = killReceivers;

    public bool SawReceiver { get; set; }

    public ClientControllerStreams? ControllerStreams { get; set; }

    public string? LastObservedSignature { get; set; }

    public object StopGate { get; } = new();

    public bool GameStopRequested { get; set; }

    private HashSet<int> ReceiverBaseline { get; } = [];

    private List<ObservedGameProcess> OwnedReceivers { get; set; } = [];

    public void CaptureReceiverBaseline(IReadOnlyList<ObservedGameProcess> receivers)
    {
        lock (StopGate)
        {
            ReceiverBaseline.Clear();
            foreach (ObservedGameProcess receiver in receivers)
            {
                _ = ReceiverBaseline.Add(receiver.ProcessId);
            }
        }
    }

    public void UpdateOwnedReceivers(IReadOnlyList<ObservedGameProcess> receivers)
    {
        if (LaunchedProcess is null)
        {
            return;
        }

        lock (StopGate)
        {
            OwnedReceivers = [.. receivers.Where(receiver => !ReceiverBaseline.Contains(receiver.ProcessId))];
        }
    }

    public IReadOnlyList<ObservedGameProcess> GetOwnedReceiversSnapshot()
    {
        lock (StopGate)
        {
            return [.. OwnedReceivers];
        }
    }
}
