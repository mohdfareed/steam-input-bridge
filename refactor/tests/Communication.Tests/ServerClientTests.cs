using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VirtualMouse.Client;
using VirtualMouse.Protocol;
using VirtualMouse.Server;

namespace Communication.Tests;

[TestClass]
public sealed class ServerClientTests
{
    [TestMethod]
    public async Task ClientConnectRegistersWithServer()
    {
        ConnectionOptions options = CreateOptions();
        using CancellationTokenSource serverStop = new();
        VirtualMouseServer server = CreateServer(options);
        Task serverTask = server.RunAsync(serverStop.Token);

        await using VirtualMouseClient client = CreateClient(options);
        await client.ConnectAsync(CancellationToken.None).ConfigureAwait(false);

        await WaitUntilAsync(() => server.Clients.Count == 1).ConfigureAwait(false);
        ConnectedClient connected = server.Clients.Single();

        Assert.AreEqual(client.ClientId, connected.Id);
        Assert.AreEqual(Environment.ProcessId, connected.ProcessId);

        await client.DisposeAsync().ConfigureAwait(false);
        await WaitUntilAsync(() => server.Clients.Count == 0).ConfigureAwait(false);
        await StopServerAsync(serverStop, serverTask).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task ClientReconnectsAfterServerRestart()
    {
        ConnectionOptions options = CreateOptions();
        using CancellationTokenSource serverOneStop = new();
        VirtualMouseServer serverOne = CreateServer(options);
        Task serverOneTask = serverOne.RunAsync(serverOneStop.Token);

        await using VirtualMouseClient client = CreateClient(options);
        await client.ConnectAsync(CancellationToken.None).ConfigureAwait(false);
        Guid firstClientId = client.ClientId.GetValueOrDefault();
        await WaitUntilAsync(() => serverOne.Clients.Count == 1).ConfigureAwait(false);

        using CancellationTokenSource clientStop = new();
        Task clientWait = client.WaitAsync(clientStop.Token);

        await StopServerAsync(serverOneStop, serverOneTask).ConfigureAwait(false);

        using CancellationTokenSource serverTwoStop = new();
        VirtualMouseServer serverTwo = CreateServer(options);
        Task serverTwoTask = serverTwo.RunAsync(serverTwoStop.Token);

        await WaitUntilAsync(() => serverTwo.Clients.Count == 1).ConfigureAwait(false);
        Assert.AreNotEqual(firstClientId, client.ClientId);

        await clientStop.CancelAsync().ConfigureAwait(false);
        await IgnoreCancellationAsync(clientWait).ConfigureAwait(false);
        await StopServerAsync(serverTwoStop, serverTwoTask).ConfigureAwait(false);
    }

    private static ConnectionOptions CreateOptions()
    {
        return new ConnectionOptions
        {
            PipeName = "VirtualMouse.Refactor.Tests." + Guid.NewGuid().ToString("N"),
            KeepAliveMilliseconds = 25,
            ReconnectDelayMilliseconds = 25,
        };
    }

    private static VirtualMouseClient CreateClient(ConnectionOptions options)
    {
        return new VirtualMouseClient(Options.Create(options), NullLogger<VirtualMouseClient>.Instance);
    }

    private static VirtualMouseServer CreateServer(ConnectionOptions options)
    {
        return new VirtualMouseServer(Options.Create(options), NullLogger<VirtualMouseServer>.Instance);
    }

    private static async Task StopServerAsync(CancellationTokenSource stop, Task serverTask)
    {
        await stop.CancelAsync().ConfigureAwait(false);
        await IgnoreCancellationAsync(serverTask).ConfigureAwait(false);
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

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token).ConfigureAwait(false);
        }
    }
}
