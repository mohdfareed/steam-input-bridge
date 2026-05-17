using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Hosting;
using Inputs;
using Inputs.Sdl;
using Outputs.Viiper;

internal static class ClientCommands
{
    // MARK: Commands
    // ========================================================================

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

    private static Command CreateRunCommand()
    {
        Command command = new("run", "Open a client session until cancelled.");
        Option<bool> mouseOption = new("--mouse")
        {
            Description = "Enable mouse forwarding for this client session.",
        };
        Option<bool> pauseOption = CliOptions.CreatePauseOption();
        command.Options.Add(mouseOption);
        command.Options.Add(pauseOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            bool mouse = parseResult.GetValue(mouseOption);
            try
            {
                ForwardingClientSession? session = await TryConnectAsync(mouse, cancellationToken).ConfigureAwait(false);
                if (session is null)
                {
                    return;
                }

                await using (session.ConfigureAwait(false))
                {
                    try
                    {
                        await Console.Out.WriteLineAsync($"mouse={FormatBool(mouse)}.").ConfigureAwait(false);
                        await RunGamepadLoopAsync(session, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                    }
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
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

    private static async Task<GamepadClientAttachment[]> AttachSteamControllersAsync(
        ForwardingClientSession session,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<SdlGamepadSource> controllers = SdlControllerCatalog.OpenSteamControllers();
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
                    GamepadReportClient reportClient = await session
                        .AttachSteamControllerAsync(source.Controller, cancellationToken)
                        .ConfigureAwait(false);
                    attachments.Add(new GamepadClientAttachment(source, reportClient));
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

    private static async Task RunGamepadLoopAsync(
        ForwardingClientSession session,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            GamepadClientAttachment[] gamepads = [];
            try
            {
                gamepads = await AttachSteamControllersAsync(session, cancellationToken).ConfigureAwait(false);
                await Console.Out.WriteLineAsync($"gamepads={gamepads.Length}. Ctrl+C to release.")
                    .ConfigureAwait(false);

                if (gamepads.Length == 0)
                {
                    await DelayReconnectAsync(cancellationToken).ConfigureAwait(false);
                    continue;
                }

                RunGamepads(gamepads, cancellationToken);
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
                    gamepads[i].Reports.Send(input);
                    return;
                }
            }
        }
    }

    // MARK: Helpers
    // ========================================================================

    internal static async Task<ForwardingClientSession?> TryConnectAsync(
        bool enableMouse,
        CancellationToken cancellationToken)
    {
        ForwardingClient client = new();
        try
        {
            return enableMouse
                ? await client.EnableMouseAsync(cancellationToken).ConfigureAwait(false)
                : await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            string message = enableMouse
                ? "client mouse: host not running"
                : "client: host not running";
            await Console.Error.WriteLineAsync(message).ConfigureAwait(false);
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

    private readonly record struct GamepadClientAttachment(
        SdlGamepadSource Source,
        GamepadReportClient Reports) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            Reports.Dispose();
            await Source.DisposeAsync().ConfigureAwait(false);
        }
    }
}
