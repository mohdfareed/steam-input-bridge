using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Globalization;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Hosting;
using Inputs;
using Inputs.Sdl;
using Outputs.Viiper;
using Platform.Windows;

internal static class ClientCommands
{
    // MARK: Commands
    // ========================================================================

    [SupportedOSPlatform("windows")]
    internal static Command CreateClientCommand()
    {
        Command command = new("client", "Control a running forwarding host.");
        command.Subcommands.Add(CreateRunCommand());
        command.Subcommands.Add(CreateStateCommand(
            "emulation",
            "Control global emulation forwarding.",
            HostToggleKind.Emulation));
        command.Subcommands.Add(CreateStateCommand(
            "physical-motion",
            "Control global physical motion forwarding.",
            HostToggleKind.PhysicalMotion));
        return command;
    }

    [SupportedOSPlatform("windows")]
    private static Command CreateRunCommand()
    {
        Command command = new("run", "Run a configured profile.");
        Argument<string> profileArgument = new("profile")
        {
            Description = "Configured profile id.",
        };
        Option<bool> pauseOption = CliOptions.CreatePauseOption();
        command.Arguments.Add(profileArgument);
        command.Options.Add(pauseOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string profileId = parseResult.GetValue(profileArgument) ?? string.Empty;
            try
            {
                ForwardingClientConnection? connection = await TryConnectAsync(cancellationToken).ConfigureAwait(false);
                if (connection is null)
                {
                    return;
                }

                await using (connection.ConfigureAwait(false))
                {
                    await RunProfileAsync(connection, profileId, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception) when (
                exception is IOException or
                    InvalidOperationException or
                    KeyNotFoundException or
                    NotSupportedException or
                    TimeoutException or
                    UnauthorizedAccessException)
            {
                await Console.Error.WriteLineAsync($"client: {exception.Message}").ConfigureAwait(false);
                Environment.ExitCode = 1;
            }
            finally
            {
                await PauseIfRequestedAsync(parseResult.GetValue(pauseOption), CancellationToken.None)
                    .ConfigureAwait(false);
            }
        });

        return command;
    }

    private static Command CreateStateCommand(
        string name,
        string description,
        HostToggleKind kind)
    {
        Command command = new(name, description);
        command.Subcommands.Add(CreateSetStateCommand("enable", enabled: true, kind));
        command.Subcommands.Add(CreateSetStateCommand("disable", enabled: false, kind));
        command.Subcommands.Add(CreateToggleStateCommand(kind));
        return command;
    }

    private static Command CreateSetStateCommand(string name, bool enabled, HostToggleKind kind)
    {
        Command command = new(name, $"{(enabled ? "Enable" : "Disable")} {DisplayDescription(kind)}.");

        command.SetAction(async (_, cancellationToken) =>
        {
            await SetHostStateAsync(kind, enabled, cancellationToken).ConfigureAwait(false);
        });

        return command;
    }

    private static Command CreateToggleStateCommand(HostToggleKind kind)
    {
        Command command = new("toggle", $"Toggle {DisplayDescription(kind)}.");

        command.SetAction(async (_, cancellationToken) =>
        {
            await ToggleHostStateAsync(kind, cancellationToken).ConfigureAwait(false);
        });

        return command;
    }

    // MARK: Profile Run
    // ========================================================================

    [SupportedOSPlatform("windows")]
    private static async Task RunProfileAsync(
        ForwardingClientConnection connection,
        string profileId,
        CancellationToken cancellationToken)
    {
        ClientRunInfo run = await connection
            .StartRunAsync(
                new ClientRunRequest(profileId, Environment.ProcessId, TryGetSteamAppId()),
                cancellationToken)
            .ConfigureAwait(false);

        using GameProcessHost game = new();
        using CancellationTokenSource runCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task? controllerTask = null;

        try
        {
            int processId = game.Launch(run.Executable, run.Arguments, run.WorkingDirectory);
            await connection.ActivateRunAsync(run.RunId, processId, cancellationToken).ConfigureAwait(false);
            controllerTask = RunClientControllerLoopAsync(connection, run.RunId, runCancellation.Token);
            await Console.Out.WriteLineAsync(
                    $"profile={run.ProfileId} title=\"{run.Title}\" pid={processId}")
                .ConfigureAwait(false);

            await WaitForGameExitAsync(game, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (cancellationToken.IsCancellationRequested)
            {
                game.Stop();
            }

            await runCancellation.CancelAsync().ConfigureAwait(false);
            if (controllerTask is not null)
            {
                await ObserveTaskAsync(controllerTask, runCancellation.Token).ConfigureAwait(false);
            }

            await connection.EndRunAsync(run.RunId, CancellationToken.None).ConfigureAwait(false);
        }
    }

    [SupportedOSPlatform("windows")]
    private static async Task WaitForGameExitAsync(
        GameProcessHost game,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (!game.IsTreeRunning)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task RunClientControllerLoopAsync(
        ForwardingClientConnection connection,
        Guid runId,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            GamepadClientAttachment[] gamepads = [];
            try
            {
                gamepads = await AttachClientControllersAsync(connection, runId, cancellationToken)
                    .ConfigureAwait(false);
                await Console.Out.WriteLineAsync($"gamepads={gamepads.Length}").ConfigureAwait(false);

                if (gamepads.Length == 0)
                {
                    await DelayReconnectAsync(cancellationToken).ConfigureAwait(false);
                    continue;
                }

                RunGamepads(gamepads, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (SdlGamepadDisconnectedException exception)
            {
                await Console.Error.WriteLineAsync($"client gamepad: {exception.Message}; reconnecting.")
                    .ConfigureAwait(false);
            }
            finally
            {
                foreach (GamepadClientAttachment gamepad in gamepads)
                {
                    await gamepad.DisposeAsync().ConfigureAwait(false);
                }
            }

            await DelayReconnectAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<GamepadClientAttachment[]> AttachClientControllersAsync(
        ForwardingClientConnection connection,
        Guid runId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<SdlGamepadSource> controllers = SdlControllerCatalog.OpenClientControllers();
        List<GamepadClientAttachment> attachments = [];

        try
        {
            foreach (SdlGamepadSource controller in controllers)
            {
                if (ViiperXbox360Output.IsOwnedSdlDevice(
                    controller.Controller.Name,
                    controller.Controller.Path))
                {
                    await controller.DisposeAsync().ConfigureAwait(false);
                    continue;
                }

                SdlGamepadSource? source = controller;
                try
                {
                    ControllerRouteClient stream = await connection
                        .AttachControllerRouteAsync(runId, source.Controller, cancellationToken)
                        .ConfigureAwait(false);
                    attachments.Add(new GamepadClientAttachment(source, stream));
                    source = null;
                    await Console.Out.WriteLineAsync($"gamepad attached: {DisplayController(controller.Controller)}")
                        .ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    await Console.Error.WriteLineAsync(
                            $"gamepad skipped: {DisplayController(controller.Controller)} ({exception.Message})")
                        .ConfigureAwait(false);
                }
                finally
                {
                    if (source is not null)
                    {
                        await source.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }

            return [.. attachments];
        }
        catch
        {
            foreach (GamepadClientAttachment attachment in attachments)
            {
                await attachment.DisposeAsync().ConfigureAwait(false);
            }

            throw;
        }
    }

    private static void RunGamepads(
        IReadOnlyList<GamepadClientAttachment> gamepads,
        CancellationToken cancellationToken)
    {
        SdlGamepadSource[] sources = new SdlGamepadSource[gamepads.Count];
        for (int i = 0; i < gamepads.Count; i++)
        {
            sources[i] = gamepads[i].Source;
        }

        SdlControllerInputLoop.Run(sources, SendGamepad, cancellationToken);

        void SendGamepad(SdlGamepadSource source, GamepadInput input)
        {
            for (int i = 0; i < gamepads.Count; i++)
            {
                if (ReferenceEquals(gamepads[i].Source, source))
                {
                    gamepads[i].Stream.Send(input);
                    return;
                }
            }
        }
    }

    // MARK: Privates
    // ========================================================================

    internal static async Task<ForwardingClientConnection?> TryConnectAsync(CancellationToken cancellationToken)
    {
        ForwardingClient client = new();
        try
        {
            return await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            await Console.Error.WriteLineAsync("client: host not running").ConfigureAwait(false);
            Environment.ExitCode = 1;
            return null;
        }
        catch (IOException exception)
        {
            await Console.Error.WriteLineAsync($"client: unavailable ({exception.Message})").ConfigureAwait(false);
            Environment.ExitCode = 1;
            return null;
        }
    }

    private static uint? TryGetSteamAppId()
    {
        foreach (string variable in new[] { "SteamAppId", "SteamGameId" })
        {
            string? value = Environment.GetEnvironmentVariable(variable);
            if (uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint appId))
            {
                return appId;
            }
        }

        return null;
    }

    private static async Task SetHostStateAsync(
        HostToggleKind kind,
        bool enabled,
        CancellationToken cancellationToken)
    {
        ForwardingClient client = new();
        try
        {
            await SetHostStateAsync(client, kind, enabled, cancellationToken).ConfigureAwait(false);
            await Console.Out.WriteLineAsync($"{DisplayStatusKey(kind)}={FormatBool(enabled)}").ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            await Console.Error.WriteLineAsync($"client {DisplayCommandName(kind)}: host not running").ConfigureAwait(false);
            Environment.ExitCode = 1;
        }
        catch (IOException exception)
        {
            await Console.Error.WriteLineAsync(
                $"client {DisplayCommandName(kind)}: unavailable ({exception.Message})")
                .ConfigureAwait(false);
            Environment.ExitCode = 1;
        }
    }

    private static async Task ToggleHostStateAsync(
        HostToggleKind kind,
        CancellationToken cancellationToken)
    {
        ForwardingClient client = new();
        try
        {
            bool enabled = await ToggleHostStateAsync(client, kind, cancellationToken).ConfigureAwait(false);
            await Console.Out.WriteLineAsync($"{DisplayStatusKey(kind)}={FormatBool(enabled)}").ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            await Console.Error.WriteLineAsync($"client {DisplayCommandName(kind)}: host not running").ConfigureAwait(false);
            Environment.ExitCode = 1;
        }
        catch (IOException exception)
        {
            await Console.Error.WriteLineAsync(
                $"client {DisplayCommandName(kind)}: unavailable ({exception.Message})")
                .ConfigureAwait(false);
            Environment.ExitCode = 1;
        }
    }

    private static Task SetHostStateAsync(
        ForwardingClient client,
        HostToggleKind kind,
        bool enabled,
        CancellationToken cancellationToken)
    {
        return kind switch
        {
            HostToggleKind.Emulation => client.SetEmulationEnabledAsync(enabled, cancellationToken),
            HostToggleKind.PhysicalMotion => client.SetPhysicalMotionEnabledAsync(enabled, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    private static Task<bool> ToggleHostStateAsync(
        ForwardingClient client,
        HostToggleKind kind,
        CancellationToken cancellationToken)
    {
        return kind switch
        {
            HostToggleKind.Emulation => client.ToggleEmulationEnabledAsync(cancellationToken),
            HostToggleKind.PhysicalMotion => client.TogglePhysicalMotionEnabledAsync(cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    private static async Task ObserveTaskAsync(Task task, CancellationToken cancellationToken)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (IOException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static string DisplayCommandName(HostToggleKind kind)
    {
        return kind switch
        {
            HostToggleKind.Emulation => "emulation",
            HostToggleKind.PhysicalMotion => "physical-motion",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    private static string DisplayDescription(HostToggleKind kind)
    {
        return kind switch
        {
            HostToggleKind.Emulation => "global emulation forwarding",
            HostToggleKind.PhysicalMotion => "global physical motion forwarding",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    private static string DisplayStatusKey(HostToggleKind kind)
    {
        return kind switch
        {
            HostToggleKind.Emulation => "emulationEnabled",
            HostToggleKind.PhysicalMotion => "physicalMotionEnabled",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    private static string FormatBool(bool value)
    {
        return value ? "true" : "false";
    }

    private static string DisplayController(SdlControllerInfo controller)
    {
        return controller.Source == SdlControllerSource.Steam
            ? $"{controller.Name} (steam)"
            : controller.Name;
    }

    private static async Task PauseIfRequestedAsync(bool pause, CancellationToken cancellationToken)
    {
        if (!pause)
        {
            return;
        }

        await Console.Out.WriteLineAsync("press Enter to exit").ConfigureAwait(false);
        _ = await Console.In.ReadLineAsync(cancellationToken).ConfigureAwait(false);
    }

    private static Task DelayReconnectAsync(CancellationToken cancellationToken)
    {
        return Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
    }

    private enum HostToggleKind
    {
        Emulation,
        PhysicalMotion,
    }

    private sealed class GamepadClientAttachment : IAsyncDisposable
    {
        private readonly CancellationTokenSource _feedbackCancellation = new();
        private readonly Task _feedbackTask;

        public GamepadClientAttachment(SdlGamepadSource source, ControllerRouteClient stream)
        {
            Source = source;
            Stream = stream;
            _feedbackTask = Task.Run(RunFeedback, CancellationToken.None);
        }

        public SdlGamepadSource Source { get; }

        public ControllerRouteClient Stream { get; }

        public async ValueTask DisposeAsync()
        {
            await _feedbackCancellation.CancelAsync().ConfigureAwait(false);
            Stream.Dispose();
            await ObserveFeedbackAsync().ConfigureAwait(false);
            await Source.DisposeAsync().ConfigureAwait(false);
            _feedbackCancellation.Dispose();
        }

        private void RunFeedback()
        {
            Stream.RunFeedback(
                rumble => _ = Source.TryRumble(rumble),
                _feedbackCancellation.Token);
        }

        private async Task ObserveFeedbackAsync()
        {
            try
            {
                await _feedbackTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_feedbackCancellation.IsCancellationRequested)
            {
            }
            catch (IOException) when (_feedbackCancellation.IsCancellationRequested)
            {
            }
            catch (ObjectDisposedException) when (_feedbackCancellation.IsCancellationRequested)
            {
            }
        }
    }
}
