using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SteamInputBridge.Forwarding;
using SteamInputBridge.Forwarding.Controller;
using SteamInputBridge.Forwarding.Controller.Routing;
using SteamInputBridge.Forwarding.Mouse;
using SteamInputBridge.Hosting;
using SteamInputBridge.Hosting.Client.Connection;
using SteamInputBridge.Hosting.Server.Orchestration;
using SteamInputBridge.Hosting.Server.Orchestration.Active;
using SteamInputBridge.Hosting.Server.Orchestration.Lifetime;
using SteamInputBridge.Hosting.Server.Pipes;
using SteamInputBridge.Runtime;
using SteamInputBridge.Settings;
using SteamInputBridge.Settings.Profiles;
using ForwardingControllerOutput = SteamInputBridge.Forwarding.Controller.ControllerOutput;
using ForwardingMouseOutput = SteamInputBridge.Forwarding.Mouse.MouseOutput;

namespace SteamInputBridge.Tests;

/// <summary>Tests Hosting integration with controller forwarding.</summary>
[TestClass]
public sealed class HostingForwardingTests
{
    /// <summary>Checks client controller pipe input reaches active forwarding output and feedback returns.</summary>
    [TestMethod]
    public async Task ClientControllerPipeFeedsActiveForwardingOutput()
    {
        string directory = Path.Combine(Path.GetTempPath(), "SteamInputBridge.Tests", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);
        string settingsPath = Path.Combine(directory, "appsettings.json");
        string pipeName = NewPipeName();
        await File.WriteAllTextAsync(settingsPath, SettingsJson()).ConfigureAwait(false);

        try
        {
            using ServiceProvider services = CreateServices(settingsPath);
            ActiveClientRegistry runtime = new();
            FakeControllerOutputFactory factory = new();
            await using ControllerBroker broker = new(factory);
            int foregroundProcessId = 0;

            ServerActiveClientLoop activeClients = new(
                runtime,
                () => Volatile.Read(ref foregroundProcessId),
                TimeSpan.FromMilliseconds(5),
                args => broker.SetActiveClient(args.CurrentClientId));

            await using ServerService server = new(
                NullLogger<ServerService>.Instance,
                settingsFile: null,
                services.GetRequiredService<ProfilesService>(),
                runtime,
                activeClients,
                broker,
                pipeName: pipeName);

            using CancellationTokenSource serverStop = new();
            Task serverTask = server.RunAsync(serverStop.Token);
            await using ClientService client = new(NullLoggerFactory.Instance, pipeName);

            try
            {
                await client.ConnectAsync(CancellationToken.None).ConfigureAwait(false);
                ClientRunLaunch launch = await client
                    .RegisterRunAsync(new RegisterRunRequest("game", SteamAppId: 123), CancellationToken.None)
                    .ConfigureAwait(false);
                ControllerId controllerId = new("physical-1", "Physical 1");
                broker.UpdatePhysicalController(
                    controllerId,
                    ControllerState.Empty,
                    ControllerFeatures.Rumble);
                await client.RegisterClientControllersAsync(
                        [new ClientControllerInfo(
                            0,
                            "physical-1",
                            "Physical 1",
                            ControllerFeatures.StandardControls | ControllerFeatures.Rumble)],
                        CancellationToken.None)
                    .ConfigureAwait(false);

                using NamedPipeClientStream controllerPipe = new(
                    ".",
                    launch.ControllerPipeName,
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous);
                await controllerPipe.ConnectAsync(TimeSpan.FromSeconds(5), CancellationToken.None)
                    .ConfigureAwait(false);

                await client.UpdateRunProcessesAsync(
                        [new ObservedGameProcess(321, "Game.exe")],
                        CancellationToken.None)
                    .ConfigureAwait(false);
                Volatile.Write(ref foregroundProcessId, 321);
                await WaitUntilAsync(() => broker.GetStatus().ActiveClientId == client.ClientId)
                    .ConfigureAwait(false);

                ControllerPipeWriter writer = new(controllerPipe);
                await writer.WriteInputAsync(new ControllerInputFrame(
                        0,
                        new ControllerState(
                            new ControllerStandardState(ControllerButtons.South, 1, 2, 3, 4, 5, 6),
                            null,
                            null)))
                    .ConfigureAwait(false);

                try
                {
                    await WaitUntilAsync(() =>
                        TryFindOutput(factory, controllerId, out FakeControllerOutput? candidate) &&
                        candidate is not null &&
                        candidate.LastState.Standard?.Buttons == ControllerButtons.South)
                        .ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    ControllerBrokerStatus status = broker.GetStatus();
                    Assert.Fail(
                        "Timed out waiting for controller pipe input. " +
                        $"outputs={factory.Outputs.Count}, " +
                        $"lastButtons={FormatButtons(factory.Outputs)}, " +
                        $"activeClient={status.ActiveClientId}, " +
                        $"slots={status.Slots.Count}.");
                }

                FakeControllerOutput output = FindOutput(factory, controllerId);

                await client.RegisterClientControllersAsync(
                        [new ClientControllerInfo(
                            0,
                            "physical-1",
                            "Physical 1",
                            ControllerFeatures.StandardControls | ControllerFeatures.Rumble)],
                        CancellationToken.None)
                    .ConfigureAwait(false);

                Assert.AreSame(output, FindOutput(factory, controllerId));
                Assert.IsFalse(output.Disposed);

                output.EmitFeedback(new ControllerFeedback(new ControllerRumble(10, 20)));
                ControllerPipeMessage feedback = await new ControllerPipeReader(controllerPipe)
                    .ReadAsync(CancellationToken.None)
                    .AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);

                Assert.AreEqual(ControllerPipeFrameType.Feedback, feedback.Type);
                Assert.AreEqual((ushort)10, feedback.Feedback.Feedback.Rumble?.LowFrequency);
            }
            finally
            {
                await serverStop.CancelAsync().ConfigureAwait(false);
                await IgnoreCancellationAsync(serverTask.WaitAsync(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Server-side physical matching moves a Steam route onto the physical slot.</summary>
    [TestMethod]
    public async Task ControllerPipeReresolvesSteamRouteWhenPhysicalMatchAppears()
    {
        Guid clientId = Guid.NewGuid();
        FakeControllerOutputFactory factory = new();
        FakePhysicalControllerResolver resolver = new();
        await using ControllerBroker broker = new(factory);
        await using ClientControllerPipe pipe = new(
            clientId,
            "unused",
            broker,
            NullLogger.Instance,
            resolver);

        broker.RegisterClient(clientId, ForwardingControllerOutput.Xbox360);
        resolver.Reject = true;
        _ = pipe.RegisterControllers(
            [new ClientControllerInfo(
                0,
                "steam:05de143a9a0d5235",
                "Steam Controller",
                ControllerFeatures.StandardControls,
                PhysicalDeviceId: null,
                VendorId: 0x28de,
                ProductId: 0x1302)]);

        Assert.IsEmpty(factory.Outputs);

        resolver.Reject = false;
        resolver.Resolved = new ClientControllerInfo(
            0,
            @"path:\\?\hid#vid_28de&pid_1142",
            "Steam Controller",
            ControllerFeatures.StandardControls,
            @"path:\\?\hid#vid_28de&pid_1142",
            0x28de,
            0x1142);
        broker.UpdatePhysicalController(
            new ControllerId(@"path:\\?\hid#vid_28de&pid_1142", "Steam Controller"),
            ControllerState.Empty,
            ControllerFeatures.StandardControls);
        _ = pipe.RefreshResolvedControllers();

        Assert.HasCount(1, factory.Outputs);
        Assert.AreEqual(@"path:\\?\hid#vid_28de&pid_1142", factory.Outputs[0].ControllerId.Value);
    }

    /// <summary>Unresolved Steam-routed DS4-shaped streams do not create output slots.</summary>
    [TestMethod]
    public async Task ControllerPipeDropsUnresolvedSteamDs4()
    {
        Guid clientId = Guid.NewGuid();
        FakeControllerOutputFactory factory = new();
        FakePhysicalControllerResolver resolver = new() { Reject = true };
        await using ControllerBroker broker = new(factory);
        await using ClientControllerPipe pipe = new(
            clientId,
            "unused",
            broker,
            NullLogger.Instance,
            resolver);

        broker.RegisterClient(clientId, ForwardingControllerOutput.Ds4);
        ControllerRegistrationResult registration = pipe.RegisterControllers(
            [new ClientControllerInfo(
                0,
                "steam:0654c5c41534ef2f",
                "PS4 Controller",
                ControllerFeatures.StandardControls | ControllerFeatures.Rumble,
                PhysicalDeviceId: null,
                VendorId: 0x054c,
                ProductId: 0x05c4)]);

        Assert.IsEmpty(registration.Controllers);
        Assert.IsEmpty(broker.GetStatus().Slots);
        Assert.IsEmpty(factory.Outputs);
    }

    /// <summary>A real Steam Controller stream can create a client-only output slot.</summary>
    [TestMethod]
    public async Task ControllerPipeKeepsUnmatchedSteamControllerAsClientOnlySlot()
    {
        Guid clientId = Guid.NewGuid();
        FakeControllerOutputFactory factory = new();
        await using ControllerBroker broker = new(factory);
        await using PhysicalControllerPump pump = new(broker, NullLogger.Instance);
        await using ClientControllerPipe pipe = new(
            clientId,
            "unused",
            broker,
            NullLogger.Instance,
            pump);

        broker.RegisterClient(clientId, ForwardingControllerOutput.Ds4);
        ControllerRegistrationResult registration = pipe.RegisterControllers(
            [new ClientControllerInfo(
                0,
                "steam:0001fa99604010e6",
                "Steam Controller",
                ControllerFeatures.StandardControls | ControllerFeatures.Touchpad,
                PhysicalDeviceId: null,
                VendorId: 0x28de,
                ProductId: 0x1302)]);

        Assert.HasCount(1, registration.Controllers);
        Assert.HasCount(1, factory.Outputs);
        Assert.AreEqual("steam:0001fa99604010e6", factory.Outputs[0].ControllerId.Value);
        ControllerBrokerStatus status = broker.GetStatus();
        Assert.HasCount(1, status.Slots);
        Assert.IsFalse(status.Slots[0].HasPhysicalEndpoint);
        Assert.IsTrue(status.Slots[0].OutputConnected);
    }

    /// <summary>Steam route ids stay pending until the host sees a physical counterpart.</summary>
    [TestMethod]
    public async Task PhysicalResolverRejectsUnmatchedSteamRoute()
    {
        await using ControllerBroker broker = new(new FakeControllerOutputFactory());
        await using PhysicalControllerPump pump = new(broker, NullLogger.Instance);

        ClientControllerInfo? resolved = pump.ResolveClientController(Guid.NewGuid(), new ClientControllerInfo(
            0,
            "steam:05de143a9a0d5235",
            "XInput Controller #1",
            ControllerFeatures.StandardControls,
            PhysicalDeviceId: null,
            VendorId: 0x28de,
            ProductId: 0x11ff));

        Assert.IsNull(resolved);
    }

    /// <summary>A real Steam Controller stream is allowed when no host physical counterpart exists.</summary>
    [TestMethod]
    public async Task PhysicalResolverKeepsUnmatchedSteamControllerRoute()
    {
        await using ControllerBroker broker = new(new FakeControllerOutputFactory());
        await using PhysicalControllerPump pump = new(broker, NullLogger.Instance);

        ClientControllerInfo route = new(
            0,
            "steam:0001fa99604010e6",
            "Steam Controller",
            ControllerFeatures.StandardControls | ControllerFeatures.Touchpad,
            PhysicalDeviceId: null,
            VendorId: 0x28de,
            ProductId: 0x1302);

        ClientControllerInfo? resolved = pump.ResolveClientController(Guid.NewGuid(), route);

        Assert.AreSame(route, resolved);
    }

    /// <summary>Server-side route resolver can reject client-visible physical echoes before they create outputs.</summary>
    [TestMethod]
    public async Task ControllerPipeDropsRejectedClientController()
    {
        Guid clientId = Guid.NewGuid();
        FakeControllerOutputFactory factory = new();
        FakePhysicalControllerResolver resolver = new() { Reject = true };
        await using ControllerBroker broker = new(factory);
        await using ClientControllerPipe pipe = new(
            clientId,
            "unused",
            broker,
            NullLogger.Instance,
            resolver);

        broker.RegisterClient(clientId, ForwardingControllerOutput.Ds4);
        ControllerRegistrationResult registration = pipe.RegisterControllers(
            [new ClientControllerInfo(
                0,
                @"path:\\?\hid#vid_054c&pid_05c4#virtual",
                "PS4 Controller",
                ControllerFeatures.StandardControls,
                @"path:\\?\hid#vid_054c&pid_05c4#virtual",
                0x054c,
                0x05c4)]);

        Assert.IsEmpty(registration.Controllers);
        Assert.IsEmpty(broker.GetStatus().Slots);
        Assert.IsEmpty(factory.Outputs);
    }

    /// <summary>Native-controller profiles do not create a controller stream pipe.</summary>
    [TestMethod]
    public async Task NativeControllerProfileSkipsControllerPipe()
    {
        string directory = Path.Combine(Path.GetTempPath(), "SteamInputBridge.Tests", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);
        string settingsPath = Path.Combine(directory, "appsettings.json");
        await File.WriteAllTextAsync(settingsPath, NativeControllerSettingsJson()).ConfigureAwait(false);

        try
        {
            using ServiceProvider services = CreateServices(settingsPath);
            ActiveClientRegistry runtime = new();
            using ControllerBroker broker = new(new FakeControllerOutputFactory());
            using MouseBroker mouse = new(new FakeMouseOutputFactory());
            await using ControllerPipeSessions pipes = new(broker, NullLogger.Instance);
            ServerSessions sessions = new(
                NullLogger.Instance,
                services.GetRequiredService<ProfilesService>(),
                runtime,
                broker,
                mouse,
                pipes);

            Guid clientId = sessions.ConnectClient(Environment.ProcessId);
            ClientRunLaunch launch = await sessions
                .RegisterRunAsync(clientId, new RegisterRunRequest("native", SteamAppId: null))
                .ConfigureAwait(false);

            Assert.AreEqual(string.Empty, launch.ControllerPipeName);
            Assert.IsEmpty(pipes.GetStatus());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Status reads are diagnostics only and do not publish state changes.</summary>
    [TestMethod]
    public async Task ServerStatusReadDoesNotPublishStateChange()
    {
        int statusChanges = 0;
        ActiveClientRegistry runtime = new();
        using ControllerBroker broker = new(new FakeControllerOutputFactory());
        using MouseBroker mouse = new(new FakeMouseOutputFactory());
        await using ControllerPipeSessions pipes = new(broker, NullLogger.Instance);
        ServerSessions sessions = new(
            NullLogger.Instance,
            profiles: null,
            runtime,
            broker,
            mouse,
            pipes,
            statusChanged: () => statusChanges++);

        _ = sessions.ConnectClient(Environment.ProcessId);
        statusChanges = 0;

        _ = await sessions.GetStatusAsync().ConfigureAwait(false);

        Assert.AreEqual(0, statusChanges);
    }

    /// <summary>Stopping a launched client stops only receiver pids owned by that run.</summary>
    [TestMethod]
    public async Task StopClientKillsLifecycleOwnedReceivers()
    {
        List<ObservedGameProcess> killedReceivers = [];
        List<int> killedClients = [];
        ActiveClientRegistry runtime = new();
        using ControllerBroker broker = new(new FakeControllerOutputFactory());
        using MouseBroker mouse = new(new FakeMouseOutputFactory());
        await using ControllerPipeSessions pipes = new(broker, NullLogger.Instance);
        ServerSessions sessions = new(
            NullLogger.Instance,
            profiles: null,
            runtime,
            broker,
            mouse,
            pipes,
            killProcesses: processes =>
            {
                killedReceivers.AddRange(processes);
                return processes.Count;
            },
            killProcess: processId =>
            {
                killedClients.Add(processId);
                return 1;
            });

        Guid clientId = sessions.ConnectClient(4242);
        runtime.RegisterClient(
            clientId,
            4242,
            "game",
            steamAppId: null,
            ["game.exe"],
            ownsReceiverProcesses: true);
        runtime.UpdateClient(clientId, [new ObservedGameProcess(100, "game.exe")]);

        await sessions.StopClientAsync(clientId).ConfigureAwait(false);

        Assert.AreEqual(100, killedReceivers.Single().ProcessId);
        Assert.AreEqual(4242, killedClients.Single());
        Assert.IsEmpty(runtime.GetStatus().Clients);
    }

    /// <summary>Attach-only clients do not expose receiver pids for server-side killing.</summary>
    [TestMethod]
    public async Task StopClientDoesNotKillAttachedReceivers()
    {
        List<ObservedGameProcess> killedReceivers = [];
        ActiveClientRegistry runtime = new();
        using ControllerBroker broker = new(new FakeControllerOutputFactory());
        using MouseBroker mouse = new(new FakeMouseOutputFactory());
        await using ControllerPipeSessions pipes = new(broker, NullLogger.Instance);
        ServerSessions sessions = new(
            NullLogger.Instance,
            profiles: null,
            runtime,
            broker,
            mouse,
            pipes,
            killProcesses: processes =>
            {
                killedReceivers.AddRange(processes);
                return processes.Count;
            },
            killProcess: static _ => 1);

        Guid clientId = sessions.ConnectClient(4242);
        runtime.RegisterClient(
            clientId,
            4242,
            "game",
            steamAppId: null,
            ["game.exe"],
            ownsReceiverProcesses: false);
        runtime.UpdateClient(clientId, [new ObservedGameProcess(100, "game.exe")]);

        await sessions.StopClientAsync(clientId).ConfigureAwait(false);

        Assert.IsEmpty(killedReceivers);
        Assert.IsEmpty(runtime.GetStatus().Clients);
    }

    private static ServiceProvider CreateServices(string settingsPath)
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddJsonFile(settingsPath, optional: false, reloadOnChange: true)
            .Build();
        ServiceCollection services = new();
        _ = services.AddSingleton<ILogger<ApplicationSettingsService>>(NullLogger<ApplicationSettingsService>.Instance);
        _ = services.AddSingleton<ILogger<ProfilesService>>(NullLogger<ProfilesService>.Instance);
        _ = services.AddApplicationSettings(configuration, settingsPath);
        _ = services.AddProfiles();
        return services.BuildServiceProvider();
    }

    private static string SettingsJson()
    {
        return """
        {
          "SteamInputBridge": {
            "Games": {
              "game": {
                "Title": "Game",
                "Executable": "C:\\Games\\Game.exe",
                "ControllerOutput": "Xbox360",
                "MouseOutput": "None",
                "ReceiverProcesses": [ "Game.exe" ]
              }
            }
          }
        }
        """;
    }

    private static string NativeControllerSettingsJson()
    {
        return """
        {
          "SteamInputBridge": {
            "Games": {
              "native": {
                "Title": "Native",
                "Executable": "C:\\Games\\Native.exe",
                "ControllerOutput": "None",
                "MouseOutput": "None",
                "ReceiverProcesses": [ "Native.exe" ]
              }
            }
          }
        }
        """;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token).ConfigureAwait(false);
        }
    }

    private static string NewPipeName()
    {
        return $"SteamInputBridge.Tests.{Guid.NewGuid():N}";
    }

    private static string FormatButtons(IReadOnlyList<FakeControllerOutput> outputs)
    {
        return outputs.Count == 0
            ? "none"
            : string.Join(",", outputs.Select(static output =>
                output.LastState.Standard?.Buttons.ToString() ?? "none"));
    }

    private static FakeControllerOutput FindOutput(
        FakeControllerOutputFactory factory,
        ControllerId controllerId)
    {
        return TryFindOutput(factory, controllerId, out FakeControllerOutput? output) &&
            output is not null
            ? output
            : throw new InvalidOperationException($"Expected output for {controllerId}.");
    }

    private static bool TryFindOutput(
        FakeControllerOutputFactory factory,
        ControllerId controllerId,
        out FakeControllerOutput? output)
    {
        foreach (FakeControllerOutput candidate in factory.Outputs)
        {
            if (candidate.ControllerId == controllerId)
            {
                output = candidate;
                return true;
            }
        }

        output = null;
        return false;
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

    private sealed class FakeControllerOutputFactory : IControllerOutputFactory
    {
        private readonly Lock _gate = new();
        private readonly List<FakeControllerOutput> _outputs = [];

        public IReadOnlyList<FakeControllerOutput> Outputs
        {
            get
            {
                lock (_gate)
                {
                    return [.. _outputs];
                }
            }
        }

        public IControllerOutput Connect(ControllerId controllerId, ForwardingControllerOutput output)
        {
            _ = output;
            FakeControllerOutput connected = new(controllerId);
            lock (_gate)
            {
                _outputs.Add(connected);
            }

            return connected;
        }
    }

    private sealed class FakeControllerOutput(ControllerId controllerId) : IControllerOutput
    {
        private readonly Lock _gate = new();
        private Action<ControllerFeedback>? _feedback;
        private ControllerState _lastState;
        private bool _disposed;

        public ControllerId ControllerId { get; } = controllerId;

        public ControllerState LastState => GetLastState();

        public bool Disposed => IsDisposed();

        public void Send(in ControllerState state)
        {
            lock (_gate)
            {
                _lastState = state;
            }
        }

        public IDisposable ListenFeedback(Action<ControllerFeedback> handler)
        {
            _feedback += handler;
            return new Subscription(() => _feedback -= handler);
        }

        public void EmitFeedback(ControllerFeedback feedback)
        {
            _feedback?.Invoke(feedback);
        }

        public ValueTask DisposeAsync()
        {
            lock (_gate)
            {
                _disposed = true;
            }

            return ValueTask.CompletedTask;
        }

        private ControllerState GetLastState()
        {
            lock (_gate)
            {
                return _lastState;
            }
        }

        private bool IsDisposed()
        {
            lock (_gate)
            {
                return _disposed;
            }
        }
    }

    private sealed class FakeMouseOutputFactory : IMouseOutputFactory
    {
        public IMouseOutput Connect(ForwardingMouseOutput output)
        {
            _ = output;
            throw new InvalidOperationException("Mouse output should not connect.");
        }
    }

    private sealed class FakePhysicalControllerResolver : IPhysicalControllerResolver
    {
        public ClientControllerInfo? Resolved { get; set; }

        public bool Reject { get; set; }

        public void SetClientControllers(Guid clientId, IReadOnlyList<ClientControllerInfo> controllers)
        {
            _ = clientId;
            _ = controllers;
        }

        public ClientControllerInfo? ResolveClientController(Guid clientId, ClientControllerInfo controller)
        {
            _ = clientId;
            return Reject ? null : Resolved ?? controller;
        }

        public void ObserveClientControllerInput(Guid clientId, ushort controllerIndex, ControllerState state)
        {
            _ = clientId;
            _ = controllerIndex;
            _ = state;
        }

        public void RemoveClient(Guid clientId)
        {
            _ = clientId;
        }
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        public void Dispose()
        {
            dispose();
        }
    }
}
