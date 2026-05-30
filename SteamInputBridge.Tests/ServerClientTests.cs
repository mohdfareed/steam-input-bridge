using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SteamInputBridge.Hosting;
using SteamInputBridge.Hosting.Client.Connection;
using SteamInputBridge.Hosting.Server.Orchestration;
using SteamInputBridge.Hosting.Server.Orchestration.Lifetime;
using SteamInputBridge.Runtime;
using StreamJsonRpc;

namespace SteamInputBridge.Tests;

/// <summary>Tests server/client connection behavior.</summary>
[TestClass]
public sealed class ServerClientTests
{
    /// <summary>Checks that connecting a client registers it with the server.</summary>
    [TestMethod]
    public async Task ClientConnectRegistersWithServer()
    {
        string pipeName = NewPipeName();
        using CancellationTokenSource serverStop = new();
        await using ServerService server = CreateServer(pipeName);
        Task serverTask = server.RunAsync(serverStop.Token);

        await using ClientService client = CreateClient(pipeName);
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
        string pipeName = NewPipeName();
        using CancellationTokenSource serverOneStop = new();
        await using ServerService serverOne = CreateServer(pipeName);
        Task serverOneTask = serverOne.RunAsync(serverOneStop.Token);

        await using ClientService client = CreateClient(pipeName);
        await client.ConnectAsync(CancellationToken.None).ConfigureAwait(false);
        Guid firstClientId = client.ClientId.GetValueOrDefault();
        await WaitUntilAsync(() => serverOne.Clients.Count == 1).ConfigureAwait(false);

        using CancellationTokenSource clientStop = new();
        Task clientWait = client.WaitAsync(clientStop.Token);

        await StopServerAsync(serverOneStop, serverOneTask).ConfigureAwait(false);

        using CancellationTokenSource serverTwoStop = new();
        await using ServerService serverTwo = CreateServer(pipeName);
        Task serverTwoTask = serverTwo.RunAsync(serverTwoStop.Token);

        await WaitUntilAsync(() => serverTwo.Clients.Count == 1).ConfigureAwait(false);
        Assert.AreNotEqual(firstClientId, client.ClientId);

        await clientStop.CancelAsync().ConfigureAwait(false);
        await IgnoreCancellationAsync(clientWait).ConfigureAwait(false);
        await StopServerAsync(serverTwoStop, serverTwoTask).ConfigureAwait(false);
    }

    /// <summary>Checks that a slow keepalive response does not tear down a healthy pipe.</summary>
    [TestMethod]
    public async Task ClientKeepsConnectionWhenAckTimesOut()
    {
        string pipeName = NewPipeName();
        using CancellationTokenSource serverStop = new();
        Task serverTask = RunSlowAckServerAsync(pipeName, serverStop.Token);

        await using ClientService client = CreateClient(pipeName);
        List<ClientConnectionState> states = [];
        client.ConnectionChanged += (_, args) => states.Add(args.State);
        await client.ConnectAsync(CancellationToken.None).ConfigureAwait(false);

        using CancellationTokenSource clientStop = new();
        Task clientWait = client.WaitAsync(clientStop.Token);

        await Task.Delay(TimeSpan.FromSeconds(4)).ConfigureAwait(false);

        Assert.AreEqual(ClientConnectionState.Connected, client.State);
        CollectionAssert.DoesNotContain(states, ClientConnectionState.Disconnected);

        await clientStop.CancelAsync().ConfigureAwait(false);
        await IgnoreCancellationAsync(clientWait).ConfigureAwait(false);
        await serverStop.CancelAsync().ConfigureAwait(false);
        await IgnoreCancellationAsync(serverTask).ConfigureAwait(false);
    }

    /// <summary>Checks that server status is returned over the client connection.</summary>
    [TestMethod]
    public async Task ClientCanReadServerStatus()
    {
        string pipeName = NewPipeName();
        using CancellationTokenSource serverStop = new();
        await using ServerService server = CreateServer(pipeName);
        Task serverTask = server.RunAsync(serverStop.Token);

        await using ClientService client = CreateClient(pipeName);
        await client.ConnectAsync(CancellationToken.None).ConfigureAwait(false);

        ServerStatus status = await client.GetStatusAsync(CancellationToken.None).ConfigureAwait(false);

        Assert.AreEqual(1, status.ConnectedClientCount);
        await StopServerAsync(serverStop, serverTask).ConfigureAwait(false);
    }

    /// <summary>Checks that server shutdown releases connected clients.</summary>
    [TestMethod]
    public async Task ServerStopReleasesConnectedClients()
    {
        string pipeName = NewPipeName();
        using CancellationTokenSource serverStop = new();
        await using ServerService server = CreateServer(pipeName);
        Task serverTask = server.RunAsync(serverStop.Token);

        await using ClientService client = CreateClient(pipeName);
        await client.ConnectAsync(CancellationToken.None).ConfigureAwait(false);
        await WaitUntilAsync(() => server.Clients.Count == 1).ConfigureAwait(false);

        await StopServerAsync(serverStop, serverTask).ConfigureAwait(false);

        await WaitUntilAsync(() => server.Clients.Count == 0).ConfigureAwait(false);
    }

    /// <summary>Checks that client disposal is idempotent.</summary>
    [TestMethod]
    public async Task ClientDisposeIsIdempotent()
    {
        string pipeName = NewPipeName();
        using CancellationTokenSource serverStop = new();
        await using ServerService server = CreateServer(pipeName);
        Task serverTask = server.RunAsync(serverStop.Token);

        await using ClientService client = CreateClient(pipeName);
        await client.ConnectAsync(CancellationToken.None).ConfigureAwait(false);
        await WaitUntilAsync(() => server.Clients.Count == 1).ConfigureAwait(false);

        await client.DisposeAsync().ConfigureAwait(false);
        await client.DisposeAsync().ConfigureAwait(false);

        await WaitUntilAsync(() => server.Clients.Count == 0).ConfigureAwait(false);
        await StopServerAsync(serverStop, serverTask).ConfigureAwait(false);
    }

    private static ClientService CreateClient(string pipeName)
    {
        return new ClientService(NullLoggerFactory.Instance, pipeName);
    }

    private static ServerService CreateServer(string pipeName)
    {
        return new ServerService(
            NullLogger<ServerService>.Instance,
            settingsFile: null,
            profiles: null,
            runtime: null,
            activeClients: null,
            pipeName: pipeName);
    }

    private static string NewPipeName()
    {
        return $"SteamInputBridge.Tests.{Guid.NewGuid():N}";
    }

    private static async Task RunSlowAckServerAsync(
        string pipeName,
        CancellationToken cancellationToken)
    {
        await using NamedPipeServerStream pipe = new(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        using CancellationTokenRegistration registration = cancellationToken.Register(static target =>
        {
            ((Stream)target!).Dispose();
        }, pipe);

        await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
        using JsonRpc rpc = JsonRpc.Attach(pipe, new SlowAckServer());
        await rpc.Completion.ConfigureAwait(false);
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
        catch (Exception exception) when (exception is ObjectDisposedException or IOException)
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

    private sealed class SlowAckServer : IHostServerApi
    {
        public Task<Guid> ConnectAsync(int processId)
        {
            _ = processId;
            return Task.FromResult(Guid.NewGuid());
        }

        public async Task AckAsync()
        {
            await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }

        public Task<ServerStatus> GetStatusAsync()
        {
            throw new NotSupportedException();
        }

        public Task<ClientRunLaunch> StartRunAsync(StartRunRequest request)
        {
            throw new NotSupportedException();
        }

        public Task RegisterClientControllersAsync(IReadOnlyList<ClientControllerInfo> controllers)
        {
            throw new NotSupportedException();
        }

        public Task UpdateRunProcessesAsync(IReadOnlyList<ObservedGameProcess> processes)
        {
            throw new NotSupportedException();
        }

        public Task EndRunAsync()
        {
            throw new NotSupportedException();
        }
    }
}
