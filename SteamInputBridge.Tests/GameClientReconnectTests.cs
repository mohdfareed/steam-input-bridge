using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SteamInputBridge.Forwarding.Controller;
using SteamInputBridge.Hosting;
using SteamInputBridge.Hosting.Client.Connection;
using SteamInputBridge.Hosting.Client.Run;
using SteamInputBridge.Hosting.Client.Run.Controllers;
using SteamInputBridge.Hosting.Server.Orchestration;
using SteamInputBridge.Hosting.Server.Orchestration.Lifetime;
using SteamInputBridge.Runtime;
using SteamInputBridge.Settings;
using SteamInputBridge.Settings.Profiles;

namespace SteamInputBridge.Tests;

/// <summary>Tests full client-run restoration across server restarts.</summary>
[TestClass]
public sealed class GameClientReconnectTests
{
    /// <summary>Checks that a running client re-registers its profile run after the server restarts.</summary>
    [TestMethod]
    public async Task RunningClientRestoresProfileRunAfterServerRestart()
    {
        string directory = Path.Combine(Path.GetTempPath(), "SteamInputBridge.Tests", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);
        string settingsPath = Path.Combine(directory, "appsettings.json");
        await File.WriteAllTextAsync(settingsPath, SettingsJson(CurrentProcessName())).ConfigureAwait(false);

        string pipeName = NewPipeName();
        using ServiceProvider services = CreateServices(settingsPath);
        ActiveClientRegistry runtimeOne = new();
        ActiveClientRegistry runtimeTwo = new();
        await using ClientService client = new(NullLoggerFactory.Instance, pipeName);
        await using GameClient game = new(
            client,
            services.GetRequiredService<ProfilesService>(),
            NullLogger<GameClient>.Instance);
        using CancellationTokenSource runStop = new();
        using CancellationTokenSource serverOneStop = new();
        using CancellationTokenSource serverTwoStop = new();
        await using ServerService serverOne = CreateServer(pipeName, services, runtimeOne);
        await using ServerService serverTwo = CreateServer(pipeName, services, runtimeTwo);

        Task serverOneTask = serverOne.RunAsync(serverOneStop.Token);
        Task runTask = game.RunAsync("attached", steamAppId: 123, runStop.Token);

        try
        {
            await WaitUntilAsync(() => runtimeOne.GetStatus().Clients.Count == 1).ConfigureAwait(false);
            await StopServerAsync(serverOneStop, serverOneTask).ConfigureAwait(false);

            Task serverTwoTask = serverTwo.RunAsync(serverTwoStop.Token);
            try
            {
                await WaitUntilAsync(() => runtimeTwo.GetStatus().Clients.Count == 1).ConfigureAwait(false);
                ClientStatus restored = runtimeTwo.GetStatus().Clients[0];
                Assert.AreEqual("attached", restored.ProfileId);
                Assert.AreEqual((uint)123, restored.SteamAppId);
            }
            finally
            {
                await StopServerAsync(serverTwoStop, serverTwoTask).ConfigureAwait(false);
            }
        }
        finally
        {
            await runStop.CancelAsync().ConfigureAwait(false);
            await IgnoreCancellationAsync(runTask).ConfigureAwait(false);
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Checks that a restored run also recreates its controller pipe route.</summary>
    [TestMethod]
    public async Task RunningClientRestoresControllerRouteAfterServerRestart()
    {
        string directory = Path.Combine(Path.GetTempPath(), "SteamInputBridge.Tests", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);
        string settingsPath = Path.Combine(directory, "appsettings.json");
        await File.WriteAllTextAsync(settingsPath, ControllerOutputSettingsJson(CurrentProcessName()))
            .ConfigureAwait(false);

        string pipeName = NewPipeName();
        using ServiceProvider services = CreateServices(settingsPath);
        ActiveClientRegistry runtimeOne = new();
        ActiveClientRegistry runtimeTwo = new();
        List<FakeControllerStreams> streams = [];
        await using ClientService client = new(NullLoggerFactory.Instance, pipeName);
        await using GameClient game = new(
            client,
            services.GetRequiredService<ProfilesService>(),
            NullLogger<GameClient>.Instance,
            () =>
            {
                FakeControllerStreams stream = new();
                streams.Add(stream);
                return stream;
            });
        using CancellationTokenSource runStop = new();
        using CancellationTokenSource serverOneStop = new();
        using CancellationTokenSource serverTwoStop = new();
        await using ServerService serverOne = CreateServer(pipeName, services, runtimeOne);
        await using ServerService serverTwo = CreateServer(pipeName, services, runtimeTwo);

        Task serverOneTask = serverOne.RunAsync(serverOneStop.Token);
        Task runTask = game.RunAsync("controller", steamAppId: 123, runStop.Token);

        try
        {
            await WaitUntilAsync(() => HasRegisteredControllerRoute(serverOne)).ConfigureAwait(false);
            string firstPipeName = streams[0].PipeName!;

            await StopServerAsync(serverOneStop, serverOneTask).ConfigureAwait(false);

            Task serverTwoTask = serverTwo.RunAsync(serverTwoStop.Token);
            try
            {
                await WaitUntilAsync(() =>
                    streams.Count == 2 &&
                    streams[0].Disposed &&
                    HasRegisteredControllerRoute(serverTwo)).ConfigureAwait(false);

                Assert.AreNotEqual(firstPipeName, streams[1].PipeName);
                Assert.AreEqual(client.ClientId, runtimeTwo.GetStatus().Clients[0].ClientId);
            }
            finally
            {
                await StopServerAsync(serverTwoStop, serverTwoTask).ConfigureAwait(false);
            }
        }
        finally
        {
            await runStop.CancelAsync().ConfigureAwait(false);
            await IgnoreCancellationAsync(runTask).ConfigureAwait(false);
            Directory.Delete(directory, recursive: true);
        }
    }

    private static ServerService CreateServer(
        string pipeName,
        IServiceProvider services,
        ActiveClientRegistry runtime)
    {
        return new ServerService(
            NullLogger<ServerService>.Instance,
            settingsFile: null,
            services.GetRequiredService<ProfilesService>(),
            runtime,
            activeClients: null,
            pipeName: pipeName);
    }

    private static ServiceProvider CreateServices(string settingsPath)
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddJsonFile(settingsPath, optional: false, reloadOnChange: false)
            .Build();
        ServiceCollection services = new();
        _ = services.AddSingleton<ILogger<ApplicationSettingsService>>(NullLogger<ApplicationSettingsService>.Instance);
        _ = services.AddSingleton<ILogger<ProfilesService>>(NullLogger<ProfilesService>.Instance);
        _ = services.AddApplicationSettings(configuration, settingsPath);
        _ = services.AddProfiles();
        return services.BuildServiceProvider();
    }

    private static string SettingsJson(string receiverProcess)
    {
        return $$"""
        {
          "SteamInputBridge": {
            "Games": {
              "attached": {
                "Title": "Attached",
                "ControllerOutput": "None",
                "MouseOutput": "None",
                "ReceiverProcesses": [ "{{receiverProcess}}" ]
              }
            }
          }
        }
        """;
    }

    private static string ControllerOutputSettingsJson(string receiverProcess)
    {
        return $$"""
        {
          "SteamInputBridge": {
            "Games": {
              "controller": {
                "Title": "Controller",
                "ControllerOutput": "Ds4",
                "MouseOutput": "None",
                "ReceiverProcesses": [ "{{receiverProcess}}" ]
              }
            }
          }
        }
        """;
    }

    private static string CurrentProcessName()
    {
        return Path.GetFileName(Process.GetCurrentProcess().ProcessName + ".exe");
    }

    private static string NewPipeName()
    {
        return $"SteamInputBridge.Tests.{Guid.NewGuid():N}";
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
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token).ConfigureAwait(false);
        }
    }

    private static bool HasRegisteredControllerRoute(ServerService server)
    {
        ServerStatus status = server.GetStatusAsync().GetAwaiter().GetResult();
        return status.ControllerPipes.Count == 1 &&
            status.ControllerPipes[0].Controllers.Count == 1 &&
            status.Forwarding.Slots.Count == 1;
    }

    private sealed class FakeControllerStreams : IClientControllerStreams
    {
        public string? PipeName { get; private set; }

        public bool Disposed { get; private set; }

        public Task StartAsync(
            ClientService client,
            ClientRunLaunch launch,
            CancellationToken cancellationToken)
        {
            PipeName = launch.ControllerPipeName;
            return client.RegisterClientControllersAsync(
                [new ClientControllerInfo(
                    0,
                    "steam:0001fa99604010e6",
                    "Steam Controller",
                    ControllerFeatures.StandardControls | ControllerFeatures.Touchpad,
                    PhysicalDeviceId: null,
                    VendorId: 0x28de,
                    ProductId: 0x1302)],
                cancellationToken);
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
