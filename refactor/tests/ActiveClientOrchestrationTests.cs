using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VirtualMouse.Hosting;
using VirtualMouse.Runtime;
using VirtualMouse.Settings;

namespace VirtualMouse.Tests;

/// <summary>Tests server-side active-client orchestration.</summary>
[TestClass]
public sealed class ActiveClientOrchestrationTests
{
    /// <summary>Checks foreground pid updates active-client state and fan-out events.</summary>
    [TestMethod]
    public async Task ForegroundPidUpdatesActiveClientState()
    {
        ActiveClientRegistry runtime = new();
        Guid clientId = Guid.NewGuid();
        runtime.RegisterClient(clientId, Environment.ProcessId, "game", steamAppId: 123, ["game.exe"]);
        runtime.UpdateClientProcesses(clientId, [new ObservedGameProcess(123, "game.exe")]);

        int foregroundProcessId = 0;
        List<ActiveClientChangedEventArgs> changes = [];
        ActiveClientOrchestration activeClients = new(
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
    public async Task ServerRunStartsActiveClientOrchestration()
    {
        HostingSettings options = new()
        {
            PipeName = "VirtualMouse.Refactor.Tests." + Guid.NewGuid().ToString("N"),
            ForegroundPollMilliseconds = 5,
        };
        ActiveClientRegistry runtime = new();
        int foregroundProcessId = 0;
        ActiveClientOrchestration activeClients = new(
            runtime,
            () => Volatile.Read(ref foregroundProcessId),
            TimeSpan.FromMilliseconds(options.ForegroundPollMilliseconds),
            static _ => { });
        VirtualMouseServer server = new(
            Options.Create(options),
            NullLogger<VirtualMouseServer>.Instance,
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
