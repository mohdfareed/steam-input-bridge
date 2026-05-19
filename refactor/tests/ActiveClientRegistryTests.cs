using System;
using System.Collections.Generic;
using System.Linq;
using VirtualMouse.Runtime;

namespace VirtualMouse.Tests;

/// <summary>Tests active-client registry behavior.</summary>
[TestClass]
public sealed class ActiveClientRegistryTests
{
    /// <summary>Checks that the first client to observe a receiver pid owns it.</summary>
    [TestMethod]
    public void FirstClientClaimsReceiverProcess()
    {
        ActiveClientRegistry runtime = new();
        Guid first = Start(runtime, "first");
        Guid second = Start(runtime, "second");
        ObservedGameProcess process = Receiver(100);

        runtime.UpdateClient(first, [process]);
        runtime.UpdateClient(second, [process]);

        ActiveClientRegistryStatus status = runtime.GetStatus();
        ClientStatus firstStatus = Find(status, first);
        ClientStatus secondStatus = Find(status, second);

        Assert.HasCount(1, firstStatus.OwnedProcesses);
        Assert.IsEmpty(firstStatus.BlockedProcesses);
        Assert.IsEmpty(secondStatus.OwnedProcesses);
        Assert.HasCount(1, secondStatus.BlockedProcesses);
    }

    /// <summary>Checks that one client can own multiple receiver pids.</summary>
    [TestMethod]
    public void ClientCanClaimMultipleReceiverProcesses()
    {
        ActiveClientRegistry runtime = new();
        Guid clientId = Start(runtime, "game");

        runtime.UpdateClient(clientId, [Receiver(100), Receiver(101)]);

        ClientStatus status = Find(runtime.GetStatus(), clientId);
        Assert.HasCount(2, status.OwnedProcesses);
        Assert.IsNull(status.SteamAppId);
    }

    /// <summary>Checks foreground pid activates the owning client.</summary>
    [TestMethod]
    public void ForegroundPidActivatesOwningClient()
    {
        ActiveClientRegistry runtime = new();
        Guid clientId = Start(runtime, "game");
        runtime.UpdateClient(clientId, [Receiver(100)]);

        runtime.RefreshClients(100);

        ActiveClientRegistryStatus status = runtime.GetStatus();
        Assert.AreEqual(clientId, status.ActiveClientId);
        Assert.IsTrue(Find(status, clientId).IsActive);
    }

    /// <summary>Checks unclaimed foreground pid clears the active client.</summary>
    [TestMethod]
    public void UnclaimedForegroundPidClearsActiveClient()
    {
        ActiveClientRegistry runtime = new();
        Guid clientId = Start(runtime, "game");
        runtime.UpdateClient(clientId, [Receiver(100)]);
        runtime.RefreshClients(100);

        runtime.RefreshClients(200);

        ActiveClientRegistryStatus status = runtime.GetStatus();
        Assert.IsNull(status.ActiveClientId);
    }

    /// <summary>Checks receiver ownership transfers to the oldest observer.</summary>
    [TestMethod]
    public void ClaimTransfersToOldestObserverWhenOwnerEnds()
    {
        ActiveClientRegistry runtime = new();
        Guid first = Start(runtime, "first");
        Guid second = Start(runtime, "second");
        ObservedGameProcess process = Receiver(100);
        runtime.UpdateClient(first, [process]);
        runtime.UpdateClient(second, [process]);
        runtime.RefreshClients(100);

        runtime.RemoveClient(first);

        ActiveClientRegistryStatus status = runtime.GetStatus();
        ClientStatus secondStatus = Find(status, second);
        Assert.HasCount(1, secondStatus.OwnedProcesses);
        Assert.IsEmpty(secondStatus.BlockedProcesses);
        Assert.AreEqual(second, status.ActiveClientId);
    }

    /// <summary>Checks ending an inactive run does not clear the active client.</summary>
    [TestMethod]
    public void EndingInactiveRunDoesNotClearActiveClient()
    {
        ActiveClientRegistry runtime = new();
        Guid active = Start(runtime, "active");
        Guid inactive = Start(runtime, "inactive");
        runtime.UpdateClient(active, [Receiver(100)]);
        runtime.UpdateClient(inactive, [Receiver(200)]);
        runtime.RefreshClients(100);

        runtime.RemoveClient(inactive);

        Assert.AreEqual(active, runtime.GetStatus().ActiveClientId);
    }

    /// <summary>Checks ending a client releases all of its runs.</summary>
    [TestMethod]
    public void EndingClientReleasesRunsAndClaims()
    {
        ActiveClientRegistry runtime = new();
        Guid clientId = Guid.NewGuid();
        runtime.RegisterClient(clientId, Environment.ProcessId, "game", steamAppId: null, ["game.exe"]);
        runtime.UpdateClient(clientId, [Receiver(100)]);

        runtime.RemoveClient(clientId);

        ActiveClientRegistryStatus status = runtime.GetStatus();
        Assert.IsEmpty(status.Clients);
        Assert.IsEmpty(status.ReceiverProcesses);
    }

    /// <summary>Checks active-client change events only fire when active state changes.</summary>
    [TestMethod]
    public void ActiveClientChangeEventFiresOnlyOnChange()
    {
        ActiveClientRegistry runtime = new();
        Guid clientId = Start(runtime, "game");
        runtime.UpdateClient(clientId, [Receiver(100)]);
        List<ActiveClientChangedEventArgs> changes = [];
        runtime.ActiveClientChanged += (_, args) => changes.Add(args);

        runtime.RefreshClients(100);
        runtime.RefreshClients(100);
        runtime.RefreshClients(0);

        Assert.HasCount(2, changes);
        Assert.AreEqual(clientId, changes[0].CurrentClientId);
        Assert.IsNull(changes[1].CurrentClientId);
    }

    private static Guid Start(ActiveClientRegistry runtime, string profileId)
    {
        Guid clientId = Guid.NewGuid();
        runtime.RegisterClient(clientId, Environment.ProcessId, profileId, steamAppId: null, ["game.exe"]);
        return clientId;
    }

    private static ObservedGameProcess Receiver(int processId)
    {
        return new ObservedGameProcess(processId, "game.exe");
    }

    private static ClientStatus Find(ActiveClientRegistryStatus status, Guid clientId)
    {
        return status.Clients.Single(client => client.ClientId == clientId);
    }
}
