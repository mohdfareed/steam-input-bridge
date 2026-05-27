using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SteamInputBridge.Hosting.Client.Connection;
using SteamInputBridge.Hosting.Client.Run;
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
        Task runTask = game.RunAsync("attached", steamAppId: 123, killReceivers: false, runStop.Token);

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
}
