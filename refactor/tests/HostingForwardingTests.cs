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
using Microsoft.Extensions.Options;
using VirtualMouse.Forwarding;
using VirtualMouse.Hosting;
using VirtualMouse.Runtime;
using VirtualMouse.Settings;
using VirtualMouse.Settings.Profiles;
using ForwardingControllerOutput = VirtualMouse.Forwarding.ControllerOutput;

namespace VirtualMouse.Tests;

/// <summary>Tests Hosting integration with controller forwarding.</summary>
[TestClass]
public sealed class HostingForwardingTests
{
    /// <summary>Checks client controller pipe input reaches active forwarding output and feedback returns.</summary>
    [TestMethod]
    public async Task ClientControllerPipeFeedsActiveForwardingOutput()
    {
        string directory = Path.Combine(Path.GetTempPath(), "VirtualMouse.Refactor.Tests", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);
        string settingsPath = Path.Combine(directory, "appsettings.json");
        await File.WriteAllTextAsync(settingsPath, SettingsJson()).ConfigureAwait(false);

        try
        {
            using ServiceProvider services = CreateServices(settingsPath);
            HostingSettings options = new()
            {
                PipeName = "VirtualMouse.Refactor.Tests." + Guid.NewGuid().ToString("N"),
                ForegroundPollMilliseconds = 5,
            };

            ActiveClientRegistry runtime = new();
            FakeControllerOutputFactory factory = new();
            await using ControllerBroker broker = new(factory);
            int foregroundProcessId = 0;

            ActiveClientOrchestration activeClients = new(
                runtime,
                () => Volatile.Read(ref foregroundProcessId),
                TimeSpan.FromMilliseconds(options.ForegroundPollMilliseconds),
                args => broker.SetActiveClient(args.CurrentClientId));

            await using VirtualMouseServer server = new(
                Options.Create(options),
                NullLogger<VirtualMouseServer>.Instance,
                settingsFile: null,
                services.GetRequiredService<ProfilesService>(),
                runtime,
                activeClients,
                broker);

            using CancellationTokenSource serverStop = new();
            Task serverTask = server.RunAsync(serverStop.Token);
            await using VirtualMouseClient client = new(
                Options.Create(options),
                NullLoggerFactory.Instance);

            try
            {
                await client.ConnectAsync(CancellationToken.None).ConfigureAwait(false);
                ClientRunLaunch launch = await client
                    .StartRunAsync(new StartRunRequest("game", SteamAppId: 123), CancellationToken.None)
                    .ConfigureAwait(false);
                await client.RegisterClientControllersAsync(
                        [new ClientControllerInfo(0, "physical-1", "Physical 1", ControllerFeatures.StandardControls)],
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

                factory.Outputs[0].EmitFeedback(new ControllerFeedback(new ControllerRumble(10, 20)));
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
          "VirtualMouse": {
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

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token).ConfigureAwait(false);
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
            return ValueTask.CompletedTask;
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
