using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SteamInputBridge.Forwarding.Controller.Routing;
using SteamInputBridge.Forwarding.Mouse;
using SteamInputBridge.HidHide;
using SteamInputBridge.Hosting;
using SteamInputBridge.Hosting.Client.Connection;
using SteamInputBridge.Hosting.Client.Run.Controllers;
using SteamInputBridge.Hosting.Server;
using SteamInputBridge.Hosting.Server.Orchestration;
using SteamInputBridge.Hosting.Server.Orchestration.Lifetime;
using SteamInputBridge.Outputs.Viiper;
using SteamInputBridge.Runtime;
using SteamInputBridge.Settings;
using SteamInputBridge.Settings.Profiles;
using SteamInputBridge.Steam;

namespace SteamInputBridge.Tests;

/// <summary>Manual end-to-end route registration test for a running profile.</summary>
[TestClass]
[TestCategory(TestCategories.Manual)]
[TestCategory(TestCategories.Dependency)]
public sealed class ManualProfileRoutingTests
{
    /// <summary>Attaches to a configured profile, opens real SDL controllers, and waits for server route status.</summary>
    [TestMethod]
    public async Task RunningProfileRegistersRealControllerRoutes()
    {
        string profileId = TestEnvironment.Require("SIB_MANUAL_PROFILE");
        int expectedControllers = TestEnvironment.GetInt("SIB_MANUAL_EXPECTED_CONTROLLERS", 1);
        bool requireActive = TestEnvironment.GetBool("SIB_MANUAL_REQUIRE_ACTIVE");
        bool requireHidHide = TestEnvironment.GetBool("SIB_MANUAL_REQUIRE_HIDHIDE");
        string settingsPath = TestEnvironment.Get("SIB_MANUAL_SETTINGS") ??
            Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(settingsPath))
        {
            Assert.Inconclusive($"Settings file does not exist: {settingsPath}");
        }

        await using ServiceProvider services =
            CreateServices(settingsPath, $"SteamInputBridge.Tests.{Guid.NewGuid():N}");
        await using ServerService server = services.GetRequiredService<ServerService>();
        await using ClientService client = services.GetRequiredService<ClientService>();
        using CancellationTokenSource serverStop = new();
        Task serverTask = server.RunAsync(serverStop.Token);

        try
        {
            await client.ConnectAsync(CancellationToken.None).ConfigureAwait(false);
            ClientRunLaunch launch = await client
                .StartRunAsync(new StartRunRequest(profileId, ResolveSteamAppId()), CancellationToken.None)
                .ConfigureAwait(false);

            await using ClientControllerStreams streams =
                new(services.GetRequiredService<ILogger<ManualProfileRoutingTests>>());
            await streams.StartAsync(client, launch, CancellationToken.None).ConfigureAwait(false);

            using CancellationTokenSource timeout =
                new(TimeSpan.FromSeconds(TestEnvironment.GetInt("SIB_MANUAL_TIMEOUT_SECONDS", 45)));
            while (!timeout.IsCancellationRequested)
            {
                IReadOnlyList<ObservedGameProcess> receivers =
                    GameProcessHost.FindReceivers(launch.ReceiverProcesses);
                await client.UpdateRunProcessesAsync(receivers, timeout.Token).ConfigureAwait(false);

                ServerStatus status = await server.GetStatusAsync().ConfigureAwait(false);
                int controllerCount = status.ControllerPipes.Sum(static pipe => pipe.Controllers.Count);
                int physicalCount = status.Inputs.Controller.SourceCount;
                int attachedOutputCount = status.Forwarding.Slots.Count(static slot =>
                    slot.HasPhysicalEndpoint &&
                    slot.ClientEndpointCount > 0 &&
                    slot.OutputConnected);
                bool activeOk = !requireActive || status.Runtime.ActiveClientId == client.ClientId;
                bool hidHideOk = !requireHidHide ||
                    (status.HidHide.Active &&
                    status.HidHide.DeviceCount >= expectedControllers);
                WriteStatus(status, receivers);

                if (controllerCount >= expectedControllers &&
                    physicalCount >= expectedControllers &&
                    attachedOutputCount >= expectedControllers &&
                    activeOk &&
                    hidHideOk)
                {
                    return;
                }

                await Task.Delay(250, timeout.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            try
            {
                await client.EndRunAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is InvalidOperationException or IOException or ObjectDisposedException)
            {
            }

            await serverStop.CancelAsync().ConfigureAwait(false);
            await IgnoreCancellationAsync(serverTask.WaitAsync(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
        }

        Assert.Fail(
            "Profile route did not register expected physical/virtual controller routes before timeout. " +
            "Start the game/profile, keep the receiver process running, connect the expected controllers, and focus the game if SIB_MANUAL_REQUIRE_ACTIVE=1.");
    }

    public TestContext TestContext { get; set; } = null!;

    private static ServiceProvider CreateServices(string settingsPath, string pipeName)
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddJsonFile(settingsPath, optional: false, reloadOnChange: false)
            .Build();
        ServiceCollection services = new();
        _ = services.AddLogging();
        _ = services.AddApplicationSettings(configuration, settingsPath);
        _ = services.AddProfiles();
        _ = services.AddApplicationServer();
        _ = services.AddApplicationClient();
        _ = services.AddSingleton(services => CreateServerService(services, pipeName));
        _ = services.AddTransient(services => new ClientService(
            services.GetRequiredService<ILoggerFactory>(),
            pipeName));
        return services.BuildServiceProvider();
    }

    private static ServerService CreateServerService(IServiceProvider services, string pipeName)
    {
        HidHideSettings hidHideSettings = services.GetRequiredService<IOptions<HidHideSettings>>().Value;
        ViiperOutputFactory viiper = services.GetRequiredService<ViiperOutputFactory>();
        return new ServerService(
            services.GetRequiredService<ILogger<ServerService>>(),
            services.GetService<SettingsFile>(),
            services.GetService<ProfilesService>(),
            services.GetRequiredService<ActiveClientRegistry>(),
            activeClients: null,
            services.GetRequiredService<ControllerBroker>(),
            services.GetRequiredService<MouseBroker>(),
            hidHideSettings.Enabled ? services.GetRequiredService<HidHideService>() : null,
            hidHideSettings.Enabled ? services.GetRequiredService<HidHideDeviceCatalog>() : null,
            services.GetRequiredService<ServerShortcutService>(),
            viiper.ReclaimDevicesAsync,
            pipeName);
    }

    private static uint? ResolveSteamAppId()
    {
        return TestEnvironment.GetUInt32("SIB_MANUAL_STEAM_APP_ID") ?? SteamInputClient.ResolveAppId();
    }

    private void WriteStatus(ServerStatus status, IReadOnlyList<ObservedGameProcess> receivers)
    {
        TestContext.WriteLine(
            $"clients={status.ConnectedClientCount} active={status.Runtime.ActiveClientId?.ToString() ?? "none"} receivers={receivers.Count} pipes={status.ControllerPipes.Count} physical={status.Inputs.Controller.SourceCount} hidhide={status.HidHide.Active}/{status.HidHide.DeviceCount}");
        foreach (ControllerSlotStatus slot in status.Forwarding.Slots)
        {
            TestContext.WriteLine(
                $"slot id={slot.ControllerId.Value} output={slot.OutputConnected}/{slot.Output} clientEndpoints={slot.ClientEndpointCount} physical={slot.HasPhysicalEndpoint} active={slot.HasActiveClientEndpoint}");
        }

        foreach (ControllerPipeStatus pipe in status.ControllerPipes)
        {
            TestContext.WriteLine(
                $"pipe client={pipe.ClientId} connected={pipe.Connected} controllers={pipe.Controllers.Count}");
            foreach (ClientControllerStatus controller in pipe.Controllers)
            {
                TestContext.WriteLine(
                    $"pipe route idx={controller.ControllerIndex} id={controller.PhysicalControllerId} physical={controller.PhysicalDeviceId ?? "none"} label={controller.Label} features={controller.Features}");
            }
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
