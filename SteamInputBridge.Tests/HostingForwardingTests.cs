using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
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
                    .StartRunAsync(new StartRunRequest("game", SteamAppId: 123), CancellationToken.None)
                    .ConfigureAwait(false);
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

                await WaitUntilAsync(() => factory.Outputs.Count == 1 &&
                    factory.Outputs[0].LastState.Standard?.Buttons == ControllerButtons.South)
                    .ConfigureAwait(false);
                FakeControllerOutput output = factory.Outputs[0];

                await client.RegisterClientControllersAsync(
                        [new ClientControllerInfo(
                            0,
                            "physical-1",
                            "Physical 1",
                            ControllerFeatures.StandardControls | ControllerFeatures.Rumble)],
                        CancellationToken.None)
                    .ConfigureAwait(false);

                Assert.HasCount(1, factory.Outputs);
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
        _ = pipe.RegisterControllers(
            [new ClientControllerInfo(
                0,
                "steam:05de143a9a0d5235",
                "DualSense Edge",
                ControllerFeatures.StandardControls,
                PhysicalDeviceId: null,
                VendorId: 0x054c,
                ProductId: 0x0df2)]);

        Assert.HasCount(1, factory.Outputs);
        Assert.AreEqual("steam:05de143a9a0d5235", factory.Outputs[0].ControllerId.Value);

        resolver.Resolved = new ClientControllerInfo(
            0,
            @"path:\\?\hid#vid_054c&pid_0df2",
            "DualSense Edge",
            ControllerFeatures.StandardControls,
            @"path:\\?\hid#vid_054c&pid_0df2",
            0x054c,
            0x0df2);
        pipe.RefreshResolvedControllers();

        Assert.HasCount(2, factory.Outputs);
        Assert.IsTrue(factory.Outputs[0].Disposed);
        Assert.AreEqual(@"path:\\?\hid#vid_054c&pid_0df2", factory.Outputs[1].ControllerId.Value);
    }

    /// <summary>Unresolved Steam-routed DS4-shaped streams are not dropped by a generic PS4 heuristic.</summary>
    [TestMethod]
    public async Task ControllerPipeKeepsUnresolvedSteamDs4ForOwnedTrackingValidation()
    {
        Guid clientId = Guid.NewGuid();
        FakeControllerOutputFactory factory = new();
        await using ControllerBroker broker = new(factory);
        await using ClientControllerPipe pipe = new(
            clientId,
            "unused",
            broker,
            NullLogger.Instance,
            new FakePhysicalControllerResolver());

        broker.RegisterClient(clientId, ForwardingControllerOutput.Ds4);
        IReadOnlyList<ClientControllerInfo> registered = pipe.RegisterControllers(
            [new ClientControllerInfo(
                0,
                "steam:0654c5c41534ef2f",
                "PS4 Controller",
                ControllerFeatures.StandardControls | ControllerFeatures.Rumble,
                PhysicalDeviceId: null,
                VendorId: 0x054c,
                ProductId: 0x05c4)]);

        Assert.HasCount(1, registered);
        Assert.HasCount(1, broker.GetStatus().Slots);
        Assert.HasCount(1, factory.Outputs);
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
        IReadOnlyList<ClientControllerInfo> registered = pipe.RegisterControllers(
            [new ClientControllerInfo(
                0,
                @"path:\\?\hid#vid_054c&pid_05c4#virtual",
                "PS4 Controller",
                ControllerFeatures.StandardControls,
                @"path:\\?\hid#vid_054c&pid_05c4#virtual",
                0x054c,
                0x05c4)]);

        Assert.IsEmpty(registered);
        Assert.IsEmpty(broker.GetStatus().Slots);
        Assert.IsEmpty(factory.Outputs);
    }

    /// <summary>Resolved physical DS4 routes are kept even though they use the same USB identity as VIIPER DS4.</summary>
    [TestMethod]
    public async Task ControllerPipeKeepsResolvedPhysicalDs4()
    {
        Guid clientId = Guid.NewGuid();
        FakeControllerOutputFactory factory = new();
        FakePhysicalControllerResolver resolver = new()
        {
            Resolved = new ClientControllerInfo(
                0,
                @"path:\\?\hid#vid_054c&pid_05c4",
                "Wireless Controller",
                ControllerFeatures.StandardControls | ControllerFeatures.Rumble,
                @"path:\\?\hid#vid_054c&pid_05c4",
                0x054c,
                0x05c4),
        };
        await using ControllerBroker broker = new(factory);
        await using ClientControllerPipe pipe = new(
            clientId,
            "unused",
            broker,
            NullLogger.Instance,
            resolver);

        broker.RegisterClient(clientId, ForwardingControllerOutput.Ds4);
        IReadOnlyList<ClientControllerInfo> registered = pipe.RegisterControllers(
            [new ClientControllerInfo(
                0,
                "steam:0654c5c41534ef2f",
                "PS4 Controller",
                ControllerFeatures.StandardControls | ControllerFeatures.Rumble,
                PhysicalDeviceId: null,
                VendorId: 0x054c,
                ProductId: 0x05c4)]);

        Assert.HasCount(1, registered);
        Assert.HasCount(1, factory.Outputs);
        Assert.AreEqual(@"path:\\?\hid#vid_054c&pid_05c4", factory.Outputs[0].ControllerId.Value);
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
                .StartRunAsync(clientId, new StartRunRequest("native", SteamAppId: null))
                .ConfigureAwait(false);

            Assert.AreEqual(string.Empty, launch.ControllerPipeName);
            Assert.IsEmpty(pipes.GetStatus());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Status reads are diagnostics only and do not refresh route state.</summary>
    [TestMethod]
    public async Task ServerStatusReadDoesNotRefreshRouteState()
    {
        int routeChanges = 0;
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
            routeStateChanged: () => routeChanges++);

        _ = sessions.ConnectClient(Environment.ProcessId);

        _ = await sessions.GetStatusAsync().ConfigureAwait(false);

        Assert.AreEqual(0, routeChanges);
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
        public List<FakeControllerOutput> Outputs { get; } = [];

        public IControllerOutput Connect(ControllerId controllerId, ForwardingControllerOutput output)
        {
            _ = output;
            FakeControllerOutput connected = new(controllerId);
            Outputs.Add(connected);
            return connected;
        }
    }

    private sealed class FakeControllerOutput(ControllerId controllerId) : IControllerOutput
    {
        private Action<ControllerFeedback>? _feedback;

        public ControllerId ControllerId { get; } = controllerId;

        public ControllerState LastState { get; private set; }

        public bool Disposed { get; private set; }

        public void Send(in ControllerState state)
        {
            LastState = state;
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
            Disposed = true;
            return ValueTask.CompletedTask;
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

        public ClientControllerInfo? ResolveClientController(ClientControllerInfo controller)
        {
            return Reject ? null : Resolved ?? controller;
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
