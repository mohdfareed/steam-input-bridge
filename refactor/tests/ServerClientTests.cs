using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VirtualMouse.Hosting;
using VirtualMouse.Settings;

namespace VirtualMouse.Tests;

/// <summary>Tests server/client connection behavior.</summary>
[TestClass]
public sealed class ServerClientTests
{
    /// <summary>Checks that connecting a client registers it with the server.</summary>
    [TestMethod]
    public async Task ClientConnectRegistersWithServer()
    {
        HostingSettings options = CreateOptions();
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

    /// <summary>Checks that a waiting client reconnects after a server restart.</summary>
    [TestMethod]
    public async Task ClientReconnectsAfterServerRestart()
    {
        HostingSettings options = CreateOptions();
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

    /// <summary>Checks that server status is returned over the client connection.</summary>
    [TestMethod]
    public async Task ClientCanReadServerStatus()
    {
        HostingSettings options = CreateOptions();
        using CancellationTokenSource serverStop = new();
        VirtualMouseServer server = CreateServer(options);
        Task serverTask = server.RunAsync(serverStop.Token);

        await using VirtualMouseClient client = CreateClient(options);
        await client.ConnectAsync(CancellationToken.None).ConfigureAwait(false);

        ServerStatus status = await client.GetStatusAsync(CancellationToken.None).ConfigureAwait(false);

        Assert.AreEqual(1, status.ConnectedClientCount);
        await StopServerAsync(serverStop, serverTask).ConfigureAwait(false);
    }

    /// <summary>Checks that server shutdown releases connected clients.</summary>
    [TestMethod]
    public async Task ServerStopReleasesConnectedClients()
    {
        HostingSettings options = CreateOptions();
        using CancellationTokenSource serverStop = new();
        VirtualMouseServer server = CreateServer(options);
        Task serverTask = server.RunAsync(serverStop.Token);

        await using VirtualMouseClient client = CreateClient(options);
        await client.ConnectAsync(CancellationToken.None).ConfigureAwait(false);
        await WaitUntilAsync(() => server.Clients.Count == 1).ConfigureAwait(false);

        await StopServerAsync(serverStop, serverTask).ConfigureAwait(false);

        await WaitUntilAsync(() => server.Clients.Count == 0).ConfigureAwait(false);
    }

    /// <summary>Checks that client disposal is idempotent.</summary>
    [TestMethod]
    public async Task ClientDisposeIsIdempotent()
    {
        HostingSettings options = CreateOptions();
        using CancellationTokenSource serverStop = new();
        VirtualMouseServer server = CreateServer(options);
        Task serverTask = server.RunAsync(serverStop.Token);

        await using VirtualMouseClient client = CreateClient(options);
        await client.ConnectAsync(CancellationToken.None).ConfigureAwait(false);
        await WaitUntilAsync(() => server.Clients.Count == 1).ConfigureAwait(false);

        await client.DisposeAsync().ConfigureAwait(false);
        await client.DisposeAsync().ConfigureAwait(false);

        await WaitUntilAsync(() => server.Clients.Count == 0).ConfigureAwait(false);
        await StopServerAsync(serverStop, serverTask).ConfigureAwait(false);
    }

    private static HostingSettings CreateOptions()
    {
        return new HostingSettings
        {
            PipeName = "VirtualMouse.Refactor.Tests." + Guid.NewGuid().ToString("N"),
            KeepAliveMilliseconds = 25,
            ReconnectDelayMilliseconds = 25,
        };
    }

    private static VirtualMouseClient CreateClient(HostingSettings options)
    {
        return new VirtualMouseClient(
            Options.Create(options),
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
    }

    private static VirtualMouseServer CreateServer(HostingSettings options)
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
