using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SteamInputBridge.Forwarding.Controller;
using SteamInputBridge.Forwarding.Mouse;
using SteamInputBridge.Hosting;
using SteamInputBridge.Inputs.Mouse;
using SteamInputBridge.Outputs.Controller;
using SteamInputBridge.Outputs.Mouse;
using SteamInputBridge.Profiles;
using SteamInputBridge.Settings;
using SteamInputBridge.Shortcuts;
using SteamInputBridge.Shortcuts.Runtime;
using StreamJsonRpc;

namespace SteamInputBridge.Tests;

[TestClass]
public sealed class ForwardingServiceTests
{
    [TestMethod]
    public async Task ServerMouseForwardingCreatesOutputForConnectedProfileAndGatesInput()
    {
        using TestProfileRuntime runtime = await TestProfileRuntime.CreateStartedAsync(MouseOutput.Viiper)
            .ConfigureAwait(false);
        TestShortcutSource shortcuts = new();
        TestMouseInputSource input = new();
        TestMouseInputSourceFactory inputFactory = new(input);
        TestMouseOutputFactory outputFactory = new();
        await using ServerMouseForwardingService service = new(
            runtime.ActiveProfiles,
            shortcuts,
            inputFactory,
            outputFactory,
            NullLogger<ServerMouseForwardingService>.Instance);

        await service.StartAsync(default).ConfigureAwait(false);
        await WaitUntilAsync(() => outputFactory.Outputs.Count == 1 && input.HandlerReady).ConfigureAwait(false);
        await runtime.WaitForActiveProfileAsync().ConfigureAwait(false);

        TestMouseOutput output = outputFactory.Outputs[0];
        int initialClearCalls = output.ClearCalls;
        input.Send(new MouseInput(new(MouseButtons.Left, 3, 4, 0), DeviceName: null));
        input.Send(new MouseInput(MouseReport.Empty, DeviceName: null));

        Assert.HasCount(2, output.Sent);
        Assert.AreEqual(MouseButtons.Left, output.Sent[0].Report.Buttons);
        Assert.AreEqual(MouseButtons.None, output.Sent[1].Report.Buttons);

        shortcuts.Raise(
            1,
            new ShortcutTargetSetting(ShortcutTarget.MousePointer, null),
            ShortcutValue.Disable,
            ShortcutPhase.Pressed);
        input.Send(new MouseInput(new(MouseButtons.Right, 5, 6, 0), DeviceName: null));

        Assert.HasCount(2, output.Sent);
        Assert.AreEqual(initialClearCalls + 1, output.ClearCalls);
        Assert.IsFalse(service.Status.PointerEnabled);

        await service.StopAsync(default).ConfigureAwait(false);
        Assert.IsTrue(output.Disposed);
    }

    [TestMethod]
    public async Task ServerMouseForwardingTransfersOutputOwnershipToClient()
    {
        using TestProfileRuntime runtime = await TestProfileRuntime.CreateStartedAsync(MouseOutput.Viiper)
            .ConfigureAwait(false);
        TestMouseInputSource input = new();
        TestMouseOutputFactory outputFactory = new();
        await using ServerMouseForwardingService service = new(
            runtime.ActiveProfiles,
            new TestShortcutSource(),
            new TestMouseInputSourceFactory(input),
            outputFactory,
            NullLogger<ServerMouseForwardingService>.Instance);

        await service.StartAsync(default).ConfigureAwait(false);
        await WaitUntilAsync(() => outputFactory.Outputs.Count == 1).ConfigureAwait(false);

        TestMouseOutput serverOutput = outputFactory.Outputs[0];
        await service.SetClientOwnsOutputAsync(
                clientOwnsOutput: true,
                clientUsesTeensy: false)
            .ConfigureAwait(false);

        Assert.IsTrue(serverOutput.Disposed);
        Assert.IsFalse(service.Status.OutputConnected);

        await service.SetClientOwnsOutputAsync(
                clientOwnsOutput: false,
                clientUsesTeensy: false)
            .ConfigureAwait(false);
        Assert.HasCount(2, outputFactory.Outputs);

        await service.StopAsync(default).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task ServerMouseForwardingPreservesAnOrderedInputTraceExactly()
    {
        using TestProfileRuntime runtime = await TestProfileRuntime.CreateStartedAsync(MouseOutput.Viiper)
            .ConfigureAwait(false);
        TestMouseInputSource input = new();
        TestMouseOutputFactory outputFactory = new();
        await using ServerMouseForwardingService service = new(
            runtime.ActiveProfiles,
            new TestShortcutSource(),
            new TestMouseInputSourceFactory(input),
            outputFactory,
            NullLogger<ServerMouseForwardingService>.Instance);

        await service.StartAsync(default).ConfigureAwait(false);
        await WaitUntilAsync(() => outputFactory.Outputs.Count == 1 && input.HandlerReady).ConfigureAwait(false);
        await runtime.WaitForActiveProfileAsync().ConfigureAwait(false);

        MouseInput[] expected =
        [
            new(new(MouseButtons.Left, 12, -34, 0), "mouse-a", 1),
            new(new(MouseButtons.Left | MouseButtons.Right, -56, 78, 1), "mouse-b", 2),
            new(new(MouseButtons.Right, 0, 0, -1), "mouse-b", 2),
            new(new(MouseButtons.Back | MouseButtons.Forward, short.MaxValue, short.MinValue, 0), "mouse-a", 1),
            new(MouseReport.Empty, "mouse-a", 1),
        ];
        foreach (MouseInput report in expected)
        {
            input.Send(in report);
        }

        CollectionAssert.AreEqual(expected, outputFactory.Outputs[0].Sent);
        await service.StopAsync(default).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task ServerControllerForwardingPublishesActiveClientOnly()
    {
        using TestProfileRuntime runtime = await TestProfileRuntime.CreateStartedAsync(
                MouseOutput.None,
                includeInactiveClient: true)
            .ConfigureAwait(false);
        await using ServerMouseForwardingService mouse = CreateMouseService(runtime.ActiveProfiles);
        ServerControllerForwardingService service = new(
            runtime.ActiveProfiles,
            runtime.Clients,
            mouse,
            NullLogger<ServerControllerForwardingService>.Instance);

        await service.StartAsync(default).ConfigureAwait(false);
        await runtime.WaitForActiveProfileAsync().ConfigureAwait(false);
        await WaitUntilAsync(() =>
                runtime.ActiveClient.SetActiveCalls.Contains(true) &&
                runtime.ActiveClient.PointerEnabledCalls.Contains(true))
            .ConfigureAwait(false);

        Assert.IsTrue(runtime.ActiveClient.SetActiveCalls.Contains(true));
        Assert.IsFalse(runtime.InactiveClient.SetActiveCalls.Contains(true));
        Assert.IsTrue(runtime.ActiveClient.PointerEnabledCalls.Contains(true));

        await service.StopAsync(default).ConfigureAwait(false);
        Assert.IsFalse(runtime.ActiveClient.SetActiveCalls[^1]);
        Assert.IsFalse(runtime.InactiveClient.SetActiveCalls[^1]);
    }

    [TestMethod]
    public async Task ServerControllerForwardingIgnoresLostClientWhenStopping()
    {
        using TestProfileRuntime runtime = await TestProfileRuntime.CreateStartedAsync(MouseOutput.None)
            .ConfigureAwait(false);
        await using ServerMouseForwardingService mouse = CreateMouseService(runtime.ActiveProfiles);
        ServerControllerForwardingService service = new(
            runtime.ActiveProfiles,
            runtime.Clients,
            mouse,
            NullLogger<ServerControllerForwardingService>.Instance);

        await service.StartAsync(default).ConfigureAwait(false);
        await runtime.WaitForActiveProfileAsync().ConfigureAwait(false);
        await WaitUntilAsync(() => runtime.ActiveClient.SetActiveCalls.Contains(true)).ConfigureAwait(false);

        runtime.ActiveClient.SetActiveException = new ConnectionLostException("lost");

        await service.StopAsync(default).ConfigureAwait(false);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(3));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token).ConfigureAwait(false);
        }
    }

    private static ServerMouseForwardingService CreateMouseService(ActiveProfileService profiles)
    {
        TestMouseInputSource input = new();
        return new(
            profiles,
            new TestShortcutSource(),
            new TestMouseInputSourceFactory(input),
            new TestMouseOutputFactory(),
            NullLogger<ServerMouseForwardingService>.Instance);
    }

    private sealed class TestMouseInputSourceFactory(TestMouseInputSource source) : IMouseInputSourceFactory
    {
        public ValueTask<IMouseInputSource> ConnectAsync(CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            return ValueTask.FromResult<IMouseInputSource>(source);
        }
    }

    private sealed class TestMouseInputSource : IMouseInputSource
    {
        private readonly ManualResetEventSlim _stopped = new();
        private MouseInputHandler? _handler;

        public bool IsConnected => true;

        public bool HandlerReady => _handler is not null;

        public void Run(MouseInputHandler handler, CancellationToken cancellationToken = default)
        {
            _handler = handler;
            using CancellationTokenRegistration registration = cancellationToken.Register(_stopped.Set);
            _stopped.Wait();
        }

        public void Send(in MouseInput input)
        {
            _handler?.Invoke(in input);
        }

        public ValueTask DisposeAsync()
        {
            _stopped.Set();
            _stopped.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestMouseOutputFactory : IMouseOutputFactory
    {
        public List<TestMouseOutput> Outputs { get; } = [];

        public ValueTask<IMouseOutput> ConnectAsync(MouseOutput output, CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            TestMouseOutput connected = new(output);
            Outputs.Add(connected);
            return ValueTask.FromResult<IMouseOutput>(connected);
        }
    }

    private sealed class TestMouseOutput(MouseOutput output) : IMouseOutput
    {
        public MouseOutput Output { get; } = output;

        public bool IsConnected => !Disposed;

        public bool Disposed { get; private set; }

        public int ClearCalls { get; private set; }

        public List<MouseInput> Sent { get; } = [];

        public ValueTask SendAsync(in MouseInput input, CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            Sent.Add(input);
            return ValueTask.CompletedTask;
        }

        public ValueTask ClearAsync(CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            ClearCalls++;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    internal sealed class TestProfileRuntime : IDisposable
    {
        private readonly TestOptionsMonitor<SteamInputBridgeSettings> _monitor;
        private readonly SettingsService _settings;
        private readonly ProfileCatalogService _catalog;

        private TestProfileRuntime(
            TestOptionsMonitor<SteamInputBridgeSettings> monitor,
            SettingsService settings,
            ProfileCatalogService catalog,
            ProfileClientsService clients,
            ActiveProfileService activeProfiles,
            TestBridgeClientApi activeClient,
            TestBridgeClientApi inactiveClient)
        {
            _monitor = monitor;
            _settings = settings;
            _catalog = catalog;
            Clients = clients;
            ActiveProfiles = activeProfiles;
            ActiveClient = activeClient;
            InactiveClient = inactiveClient;
        }

        public ProfileClientsService Clients { get; }

        public ActiveProfileService ActiveProfiles { get; }

        public TestBridgeClientApi ActiveClient { get; }

        public TestBridgeClientApi InactiveClient { get; }

        public static async Task<TestProfileRuntime> CreateStartedAsync(
            MouseOutput mouseOutput,
            bool includeInactiveClient = false)
        {
            string currentProcessName = Process.GetCurrentProcess().ProcessName;
            SteamInputBridgeSettings settings = new();
            settings.Games["active"] = new GameProfile
            {
                Title = "Active",
                MouseOutput = mouseOutput,
                ControllerOutput = ControllerOutput.Xbox360,
            };
            settings.Games["active"].ReceiverProcesses.Add(currentProcessName);
            if (includeInactiveClient)
            {
                settings.Games["inactive"] = new GameProfile
                {
                    Title = "Inactive",
                    ControllerOutput = ControllerOutput.Xbox360,
                };
                settings.Games["inactive"].ReceiverProcesses.Add("definitely-not-running-test-receiver.exe");
            }

            TestOptionsMonitor<SteamInputBridgeSettings> monitor = new(settings);
            SettingsService settingsService = new(
                monitor,
                new SettingsFile(@"C:\Tests\appsettings.json"),
                NullLogger<SettingsService>.Instance);
            ProfileCatalogService catalog = new(settingsService);
            ProfileClientsService clients = new(catalog, NullLogger<ProfileClientsService>.Instance);
            ActiveProfileService activeProfiles = new(
                catalog,
                clients,
                () => Environment.ProcessId,
                TimeSpan.FromMilliseconds(10));
            await activeProfiles.StartAsync(default).ConfigureAwait(false);

            TestBridgeClientApi activeClient = new();
            _ = await clients
                .ConnectClientAsync(Guid.NewGuid(), 10, "active", 123, activeClient)
                .ConfigureAwait(false);
            TestBridgeClientApi inactiveClient = new();
            if (includeInactiveClient)
            {
                _ = await clients
                    .ConnectClientAsync(Guid.NewGuid(), 20, "inactive", 456, inactiveClient)
                    .ConfigureAwait(false);
            }

            return new(monitor, settingsService, catalog, clients, activeProfiles, activeClient, inactiveClient);
        }

        public Task WaitForActiveProfileAsync()
        {
            return WaitUntilAsync(() => ActiveProfiles.ActiveProfile?.Id == "active");
        }

        public void Dispose()
        {
            _ = _monitor;
            ActiveProfiles.Dispose();
            Clients.Dispose();
            _catalog.Dispose();
            _settings.Dispose();
        }
    }

    internal sealed class TestBridgeClientApi : IBridgeClientApi
    {
        public List<bool> SetActiveCalls { get; } = [];

        public Exception? SetActiveException { get; set; }

        public List<bool> PointerEnabledCalls { get; } = [];

        public Task StopAsync()
        {
            return Task.CompletedTask;
        }

        public Task<BridgeClientRuntimeStatus> GetStatusAsync()
        {
            return Task.FromResult(new BridgeClientRuntimeStatus(new(
                SetActiveCalls.Count > 0 && SetActiveCalls[^1],
                steamControllers: 1,
                virtualControllers: 1)));
        }

        public Task SetActiveAsync(bool active)
        {
            if (SetActiveException is not null)
            {
                throw SetActiveException;
            }

            SetActiveCalls.Add(active);
            return Task.CompletedTask;
        }

        public Task SetMousePointerEnabledAsync(bool enabled)
        {
            PointerEnabledCalls.Add(enabled);
            return Task.CompletedTask;
        }
    }
}
