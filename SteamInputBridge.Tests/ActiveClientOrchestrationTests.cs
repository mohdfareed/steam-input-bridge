using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SteamInputBridge.Hosting.Server.Orchestration;
using SteamInputBridge.Hosting.Server.Orchestration.Active;
using SteamInputBridge.Hosting.Server.Orchestration.Lifetime;
using SteamInputBridge.Runtime;
using SteamInputBridge.Steam;

namespace SteamInputBridge.Tests;

/// <summary>Tests server-side active-client foreground tracking.</summary>
[TestClass]
public sealed class ServerActiveClientLoopTests
{
    private static readonly string[] ExpectedSteamForceUrls =
    [
        "steam://forceinputappid/0",
        "steam://forceinputappid/111",
        "steam://forceinputappid/0",
        "steam://forceinputappid/222",
        "steam://forceinputappid/0",
    ];

    /// <summary>Checks foreground pid updates active-client state and fan-out events.</summary>
    [TestMethod]
    public async Task ForegroundPidUpdatesActiveClientState()
    {
        ActiveClientRegistry runtime = new();
        Guid clientId = Guid.NewGuid();
        runtime.RegisterClient(clientId, Environment.ProcessId, "game", steamAppId: 123, ["game.exe"]);
        runtime.UpdateClient(clientId, [new ObservedGameProcess(123, "game.exe")]);

        int foregroundProcessId = 0;
        List<ActiveClientChangedEventArgs> changes = [];
        ServerActiveClientLoop activeClients = new(
            runtime,
            () => Volatile.Read(ref foregroundProcessId),
            TimeSpan.FromMilliseconds(5),
            changes.Add);

        using CancellationTokenSource stop = new();
        Task task = activeClients.RunAsync(stop.Token);

        try
        {
            Volatile.Write(ref foregroundProcessId, 123);

            await WaitUntilAsync(() => runtime.GetStatus().ActiveClientId == clientId)
                .ConfigureAwait(false);

            ActiveClientRegistryStatus activeStatus = runtime.GetStatus();
            Assert.AreEqual(123, activeStatus.ForegroundProcessId);
            Assert.AreEqual(clientId, activeStatus.ActiveClientId);
            Assert.AreEqual(123u, activeStatus.Clients[0].SteamAppId);

            Volatile.Write(ref foregroundProcessId, 0);
            await WaitUntilAsync(() => runtime.GetStatus().ActiveClientId is null)
                .ConfigureAwait(false);

            Assert.HasCount(2, changes);
            Assert.AreEqual(clientId, changes[0].CurrentClientId);
            Assert.IsNull(changes[1].CurrentClientId);
        }
        finally
        {
            await stop.CancelAsync().ConfigureAwait(false);
            await IgnoreCancellationAsync(task).ConfigureAwait(false);
        }
    }

    /// <summary>Checks the server starts foreground observation with its lifetime.</summary>
    [TestMethod]
    public async Task ServerRunStartsActiveClientLoop()
    {
        ActiveClientRegistry runtime = new();
        int foregroundProcessId = 0;
        ServerActiveClientLoop activeClients = new(
            runtime,
            () => Volatile.Read(ref foregroundProcessId),
            TimeSpan.FromMilliseconds(5),
            static _ => { });

        await using ServerService server = new(
            NullLogger<ServerService>.Instance,
            settingsFile: null,
            profiles: null,
            runtime,
            activeClients);

        using CancellationTokenSource stop = new();
        Task task = server.RunAsync(stop.Token);

        try
        {
            Volatile.Write(ref foregroundProcessId, 321);

            await WaitUntilAsync(() => runtime.GetStatus().ForegroundProcessId == 321)
                .ConfigureAwait(false);
        }
        finally
        {
            await stop.CancelAsync().ConfigureAwait(false);
            await IgnoreCancellationAsync(task).ConfigureAwait(false);
        }
    }

    /// <summary>Steam Input forcing clears the previous app before forcing the active client.</summary>
    [TestMethod]
    public async Task ActiveClientChangeClearsThenForcesSteamInput()
    {
        ActiveClientRegistry runtime = new();
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        runtime.RegisterClient(first, Environment.ProcessId, "first", steamAppId: 111, ["first.exe"]);
        runtime.RegisterClient(second, Environment.ProcessId, "second", steamAppId: 222, ["second.exe"]);
        runtime.UpdateClient(first, [new ObservedGameProcess(1111, "first.exe")]);
        runtime.UpdateClient(second, [new ObservedGameProcess(2222, "second.exe")]);

        int foregroundProcessId = 0;
        List<string> urls = [];
        SteamInputClient steam = new((url, _) =>
        {
            urls.Add(url.AbsoluteUri);
            return ValueTask.CompletedTask;
        });
        ServerActiveClientLoop activeClients = new(
            runtime,
            () => Volatile.Read(ref foregroundProcessId),
            TimeSpan.FromMilliseconds(5),
            activeClientChanged: null,
            NullLogger.Instance,
            steam);

        using CancellationTokenSource stop = new();
        Task task = activeClients.RunAsync(stop.Token);

        try
        {
            Volatile.Write(ref foregroundProcessId, 1111);
            await WaitUntilAsync(() => urls.Count >= 2).ConfigureAwait(false);

            Volatile.Write(ref foregroundProcessId, 2222);
            await WaitUntilAsync(() => urls.Count >= 4).ConfigureAwait(false);

            Volatile.Write(ref foregroundProcessId, 0);
            await WaitUntilAsync(() => urls.Count >= 5).ConfigureAwait(false);

            CollectionAssert.AreEqual(ExpectedSteamForceUrls, urls);

            ServerSteamInputStatus status = activeClients.GetSteamInputStatus();
            Assert.IsFalse(status.Forced);
            Assert.IsNull(status.AppId);
            Assert.IsNull(status.LastError);
        }
        finally
        {
            await stop.CancelAsync().ConfigureAwait(false);
            await IgnoreCancellationAsync(task).ConfigureAwait(false);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token).ConfigureAwait(false);
        }
    }

    private static async Task IgnoreCancellationAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

}
