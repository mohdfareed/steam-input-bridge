using System;
using System.Diagnostics;
using SteamInputBridge.Hosting.Client.Run.Controllers;

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
}
