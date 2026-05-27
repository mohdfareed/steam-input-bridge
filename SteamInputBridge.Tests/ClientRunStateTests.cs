using System;
using System.Collections.Generic;
using System.Diagnostics;
using SteamInputBridge.Hosting;
using SteamInputBridge.Hosting.Client.Run;
using SteamInputBridge.Runtime;
using SteamInputBridge.Settings.Profiles;

namespace SteamInputBridge.Tests;

/// <summary>Tests client-side receiver lifetime state.</summary>
[TestClass]
public sealed class ClientRunStateTests
{
    /// <summary>Launched runs ignore receiver processes that existed before launch.</summary>
    [TestMethod]
    public void LaunchedRunReportsOnlyPostLaunchReceivers()
    {
        using Process current = Process.GetCurrentProcess();
        ClientRunState state = CreateState(executable: "game.exe");
        state.LaunchedProcess = current;

        state.CaptureReceiverBaseline(
        [
            new ObservedGameProcess(10, "game.exe"),
            new ObservedGameProcess(11, "launcher.exe"),
        ]);

        IReadOnlyList<ObservedGameProcess> receivers = state.UpdateReceivers(
        [
            new ObservedGameProcess(10, "game.exe"),
            new ObservedGameProcess(11, "launcher.exe"),
            new ObservedGameProcess(12, "game.exe"),
        ]);

        Assert.HasCount(1, receivers);
        Assert.AreEqual(12, receivers[0].ProcessId);
        IReadOnlyList<ObservedGameProcess> owned = state.GetOwnedReceiversSnapshot();
        Assert.HasCount(1, owned);
        Assert.AreEqual(12, owned[0].ProcessId);
    }

    /// <summary>Launched-run cleanup keeps receivers that briefly disappear from a scan.</summary>
    [TestMethod]
    public void LaunchedRunCleanupKeepsLifetimeOwnedReceivers()
    {
        using Process current = Process.GetCurrentProcess();
        ClientRunState state = CreateState(executable: "game.exe");
        state.LaunchedProcess = current;

        state.CaptureReceiverBaseline([new ObservedGameProcess(10, "game.exe")]);
        _ = state.UpdateReceivers(
        [
            new ObservedGameProcess(10, "game.exe"),
            new ObservedGameProcess(12, "game.exe"),
        ]);

        IReadOnlyList<ObservedGameProcess> currentReceivers =
            state.UpdateReceivers([new ObservedGameProcess(10, "game.exe")]);
        IReadOnlyList<ObservedGameProcess> cleanupReceivers = state.GetOwnedReceiversSnapshot();

        Assert.IsEmpty(currentReceivers);
        Assert.HasCount(1, cleanupReceivers);
        Assert.AreEqual(12, cleanupReceivers[0].ProcessId);
    }

    /// <summary>Shutdown owns current receivers that appeared after a launcher baseline.</summary>
    [TestMethod]
    public void LaunchedRunCleanupOwnsCurrentPostBaselineReceivers()
    {
        using Process current = Process.GetCurrentProcess();
        ClientRunState state = CreateState(executable: "game.exe");
        state.LaunchedProcess = current;

        state.CaptureReceiverBaseline([new ObservedGameProcess(10, "launcher.exe")]);

        IReadOnlyList<ObservedGameProcess> cleanupReceivers = state.GetOwnedReceiversSnapshot(
        [
            new ObservedGameProcess(10, "launcher.exe"),
            new ObservedGameProcess(12, "game.exe"),
        ]);

        Assert.HasCount(1, cleanupReceivers);
        Assert.AreEqual(12, cleanupReceivers[0].ProcessId);
    }

    /// <summary>Attached runs report every observed receiver because there is no launch baseline.</summary>
    [TestMethod]
    public void AttachedRunReportsAllObservedReceivers()
    {
        ClientRunState state = CreateState(executable: null);

        IReadOnlyList<ObservedGameProcess> receivers = state.UpdateReceivers(
        [
            new ObservedGameProcess(10, "game.exe"),
            new ObservedGameProcess(11, "launcher.exe"),
        ]);

        Assert.HasCount(2, receivers);
        Assert.AreEqual(10, receivers[0].ProcessId);
        Assert.AreEqual(11, receivers[1].ProcessId);
    }

    private static ClientRunState CreateState(string? executable)
    {
        return new ClientRunState(
            new ClientRunLaunch(
                "test",
                "Test",
                executable,
                Arguments: "",
                WorkingDirectory: null,
                ReceiverProcesses: ["game.exe", "launcher.exe"],
                ControllerOutput.None,
                MouseOutput.None,
                ControllerPipeName: "unused"),
            registeredClientId: Guid.NewGuid(),
            new StartRunRequest("test", SteamAppId: null),
            killReceivers: false);
    }
}
