using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SteamInputBridge.Hosting;
using SteamInputBridge.Outputs.Controller;
using SteamInputBridge.Outputs.Mouse;
using SteamInputBridge.Profiles;
using SteamInputBridge.Settings;

namespace SteamInputBridge.Tests;

[TestClass]
public sealed class ProfileServicesTests
{
    [TestMethod]
    public async Task ProfileCatalogServiceReloadsProfiles()
    {
        TestOptionsMonitor<SteamInputBridgeSettings> monitor = new(SettingsWithProfile("one"));
        using SettingsService settings = CreateSettings(monitor);
        using ProfileCatalogService catalog = new(settings);
        int changes = 0;
        catalog.ProfilesChanged += (_, _) => changes++;

        await catalog.StartAsync(default).ConfigureAwait(false);
        monitor.Set(SettingsWithProfile("two"));

        Assert.AreEqual(1, changes);
        Assert.IsTrue(catalog.ContainsProfile("two"));
        Assert.IsFalse(catalog.ContainsProfile("one"));
    }

    [TestMethod]
    public async Task ProfileClientsServiceConnectsOneClientPerProfileAndUpdatesStatus()
    {
        TestOptionsMonitor<SteamInputBridgeSettings> monitor = new(SettingsWithProfile("game"));
        using SettingsService settings = CreateSettings(monitor);
        using ProfileCatalogService catalog = new(settings);
        using ProfileClientsService clients = new(catalog, NullLogger<ProfileClientsService>.Instance);
        using ActiveProfileService activeProfiles = new(catalog, clients);
        FakeClientApi control = new();
        Guid connectionId = Guid.NewGuid();

        ProfileClientStatus connected = await clients
            .ConnectClientAsync(connectionId, processId: 1234, "game", steamAppId: 5678, control)
            .ConfigureAwait(false);

        ProfileStatus profile = AssertSingle(activeProfiles.Profiles);
        Assert.AreEqual(connectionId, connected.ConnectionId);
        Assert.AreEqual(1234, profile.ClientProcessId);
        Assert.AreEqual<uint?>(5678, profile.EffectiveSteamAppId);
        Assert.AreEqual(MouseOutput.Viiper, profile.Definition.MouseOutput);
        Assert.AreEqual(ControllerOutput.Xbox360, profile.Definition.ControllerOutput);
        Assert.HasCount(1, control.SetActiveCalls);
        Assert.IsFalse(control.SetActiveCalls[0]);

        _ = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                async () => await clients
                    .ConnectClientAsync(Guid.NewGuid(), 4321, "game", null, new FakeClientApi())
                    .ConfigureAwait(false))
            .ConfigureAwait(false);

        _ = clients.DisconnectClient(connectionId);
        Assert.IsNull(AssertSingle(activeProfiles.Profiles).ClientProcessId);
    }

    [TestMethod]
    public async Task ActiveProfileKeepsCurrentProfileWhileNeutralAppWindowIsForeground()
    {
        const int neutralForegroundProcessId = 987654;
        int? foregroundProcessId = null;
        TestOptionsMonitor<SteamInputBridgeSettings> monitor = new(SettingsWithCurrentReceiver("game"));
        using SettingsService settings = CreateSettings(monitor);
        using ProfileCatalogService catalog = new(settings);
        using ProfileClientsService clients = new(catalog, NullLogger<ProfileClientsService>.Instance);
        using ActiveProfileService activeProfiles = new(
            catalog,
            clients,
            () => foregroundProcessId,
            TimeSpan.FromMilliseconds(10),
            neutralForegroundProcessId);

        await catalog.StartAsync(default).ConfigureAwait(false);
        await activeProfiles.StartAsync(default).ConfigureAwait(false);
        _ = await clients
            .ConnectClientAsync(Guid.NewGuid(), processId: 1234, "game", steamAppId: null, new FakeClientApi())
            .ConfigureAwait(false);

        foregroundProcessId = Environment.ProcessId;
        await WaitUntilAsync(() => activeProfiles.ActiveProfile?.Id == "game").ConfigureAwait(false);

        foregroundProcessId = neutralForegroundProcessId;
        await Task.Delay(50).ConfigureAwait(false);

        Assert.AreEqual("game", activeProfiles.ActiveProfile?.Id);
    }

    [TestMethod]
    public async Task ProfileClientsServiceActivatesAttachOnlyReceiverWhenDetected()
    {
        TestOptionsMonitor<SteamInputBridgeSettings> monitor = new(SettingsWithCurrentReceiver("game"));
        using SettingsService settings = CreateSettings(monitor);
        using ProfileCatalogService catalog = new(settings);
        ConcurrentQueue<int> activatedProcessIds = new();
        using ProfileClientsService clients = new(
            catalog,
            NullLogger<ProfileClientsService>.Instance,
            processId =>
            {
                activatedProcessIds.Enqueue(processId);
                return WindowActivationResult.Activated;
            });

        _ = await clients
            .ConnectClientAsync(Guid.NewGuid(), processId: 1234, "game", steamAppId: null, new FakeClientApi())
            .ConfigureAwait(false);

        await WaitUntilAsync(() => activatedProcessIds.Contains(Environment.ProcessId)).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task ProfileClientsServiceRetriesActivationUntilReceiverWindowExists()
    {
        TestOptionsMonitor<SteamInputBridgeSettings> monitor = new(SettingsWithCurrentReceiver("game"));
        using SettingsService settings = CreateSettings(monitor);
        using ProfileCatalogService catalog = new(settings);
        ConcurrentQueue<int> activationAttempts = new();
        using ProfileClientsService clients = new(
            catalog,
            NullLogger<ProfileClientsService>.Instance,
            processId =>
            {
                activationAttempts.Enqueue(processId);
                return activationAttempts.Count == 1
                    ? WindowActivationResult.WindowNotFound
                    : WindowActivationResult.Activated;
            });

        _ = await clients
            .ConnectClientAsync(Guid.NewGuid(), processId: 1234, "game", steamAppId: null, new FakeClientApi())
            .ConfigureAwait(false);

        await WaitUntilAsync(() => activationAttempts.Count >= 2).ConfigureAwait(false);
        CollectionAssert.AreEqual(
            new[] { Environment.ProcessId, Environment.ProcessId },
            activationAttempts.Take(2).ToArray());
    }

    [TestMethod]
    public async Task ProfileClientsServiceAttachesExistingReceiverInsteadOfLaunchingProfile()
    {
        TestOptionsMonitor<SteamInputBridgeSettings> monitor = new(SettingsWithCurrentReceiver("game"));
        monitor.CurrentValue.Games["game"].Executable = @"C:\Unavailable\launcher.exe";
        using SettingsService settings = CreateSettings(monitor);
        using ProfileCatalogService catalog = new(settings);
        ConcurrentQueue<int> activatedProcessIds = new();
        using ProfileClientsService clients = new(
            catalog,
            NullLogger<ProfileClientsService>.Instance,
            processId =>
            {
                activatedProcessIds.Enqueue(processId);
                return WindowActivationResult.Activated;
            });

        _ = await clients
            .ConnectClientAsync(
                Guid.NewGuid(),
                processId: 1234,
                "game",
                steamAppId: null,
                new FakeClientApi())
            .ConfigureAwait(false);

        await WaitUntilAsync(() => activatedProcessIds.Contains(Environment.ProcessId)).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task ProfileClientsServiceLogsReceiverActivationResultDetails()
    {
        TestOptionsMonitor<SteamInputBridgeSettings> monitor = new(SettingsWithCurrentReceiver("game"));
        using SettingsService settings = CreateSettings(monitor);
        using ProfileCatalogService catalog = new(settings);
        TestLogger<ProfileClientsService> logger = new();
        int attempts = 0;
        using ProfileClientsService clients = new(
            catalog,
            logger,
            _ =>
            {
                attempts++;
                return attempts == 1
                    ? WindowActivationResult.WindowNotFound
                    : WindowActivationResult.Rejected;
            });

        _ = await clients
            .ConnectClientAsync(Guid.NewGuid(), processId: 1234, "game", steamAppId: null, new FakeClientApi())
            .ConfigureAwait(false);

        await WaitUntilAsync(() => logger.Contains(LogLevel.Warning, "Windows rejected foreground activation"))
            .ConfigureAwait(false);
        Assert.IsTrue(logger.Contains(LogLevel.Information, "Receiver window not found yet"));
    }

    [TestMethod]
    public async Task ProfileReceiverSessionStopsClientWhenTrackedReceiverExits()
    {
        GameProfile profile = new();
        profile.ReceiverProcesses.Add("test-receiver");
        bool receiverRunning = true;
        FakeClientApi control = new();
        using ProfileReceiverSession session = new(
            "game",
            profile,
            control,
            _ => WindowActivationResult.Activated,
            NullLogger<ProfileClientsService>.Instance,
            static () => { },
            _ => receiverRunning ? [4321] : []);

        receiverRunning = false;
        session.Start();

        await WaitUntilAsync(() => control.StopCalls == 1).ConfigureAwait(false);
        Assert.IsFalse(session.StopReceiversWhenPipeCloses);
    }

    private static ProfileStatus AssertSingle(IReadOnlyList<ProfileStatus> profiles)
    {
        Assert.HasCount(1, profiles);
        return profiles[0];
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(3));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token).ConfigureAwait(false);
        }
    }

    private static SettingsService CreateSettings(TestOptionsMonitor<SteamInputBridgeSettings> monitor)
    {
        return new SettingsService(
            monitor,
            new SettingsFile(@"C:\Tests\appsettings.json"),
            NullLogger<SettingsService>.Instance);
    }

    private static SteamInputBridgeSettings SettingsWithProfile(string profileId)
    {
        SteamInputBridgeSettings settings = new();
        settings.Games[profileId] = new GameProfile
        {
            Title = "Game",
            SteamAppId = 1111,
            MouseOutput = MouseOutput.Viiper,
            ControllerOutput = ControllerOutput.Xbox360,
        };
        settings.Games[profileId].ReceiverProcesses.Add("definitely-not-running-test-receiver.exe");
        return settings;
    }

    private static SteamInputBridgeSettings SettingsWithCurrentReceiver(string profileId)
    {
        SteamInputBridgeSettings settings = SettingsWithProfile(profileId);
        settings.Games[profileId].ReceiverProcesses.Clear();
        settings.Games[profileId].ReceiverProcesses.Add(Process.GetCurrentProcess().ProcessName);
        return settings;
    }

    private sealed class FakeClientApi : IBridgeClientApi
    {
        private int _stopCalls;

        public List<bool> SetActiveCalls { get; } = [];

        public int StopCalls => Volatile.Read(ref _stopCalls);

        public Task StopAsync()
        {
            _ = Interlocked.Increment(ref _stopCalls);
            return Task.CompletedTask;
        }

        public Task<BridgeClientRuntimeStatus> GetStatusAsync()
        {
            return Task.FromResult(new BridgeClientRuntimeStatus(new(false, 0, 0)));
        }

        public Task SetActiveAsync(bool active)
        {
            SetActiveCalls.Add(active);
            return Task.CompletedTask;
        }
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        private readonly Lock _gate = new();
        private readonly List<LogEntry> _entries = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            _ = state;
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            _ = logLevel;
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (_gate)
            {
                _entries.Add(new(logLevel, eventId.Name ?? string.Empty, formatter(state, exception)));
            }
        }

        public bool Contains(LogLevel level, string messageText)
        {
            lock (_gate)
            {
                return _entries.Any(entry =>
                    entry.Level == level &&
                    entry.Message.Contains(messageText, StringComparison.Ordinal));
            }
        }

        private sealed record LogEntry(LogLevel Level, string EventName, string Message);
    }
}
