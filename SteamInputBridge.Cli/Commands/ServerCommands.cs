using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using SteamInputBridge.Cli.Host;
using SteamInputBridge.Hosting;
using SteamInputBridge.Hosting.Server;
using StreamJsonRpc;

namespace SteamInputBridge.Cli.Commands;

internal static class ServerCommands
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);

    // MARK: Commands
    // ========================================================================

    public static Command CreateCommand()
    {
        Command status = new("status", "Print server status.");
        status.Options.Add(new Option<bool>("--json") { Description = "Print status as JSON." });
        status.SetAction((parseResult, token) => PrintServerStatusAsync(parseResult.GetValue<bool>("--json"), token));

        Command run = new("run", "Run the local server.");
        run.SetAction((_, token) => RunServerAsync(token));

        Command server = new("server", "Run or inspect the local server.");
        server.Subcommands.Add(status);
        server.Subcommands.Add(run);
        return server;
    }

    // MARK: Implementation
    // ========================================================================

    private static async Task<int> RunServerAsync(CancellationToken cancellationToken)
    {
        try
        {
            using IHost host = CliHost.CreateServer();
            await host.RunAsync(cancellationToken).ConfigureAwait(false);
            return 0;
        }
        catch (ServerAlreadyRunningException exception)
        {
            await CliOutput.WriteErrorAsync(exception.Message).ConfigureAwait(false);
            return 1;
        }
    }

    private static async Task<int> PrintServerStatusAsync(bool json, CancellationToken cancellationToken)
    {
        try
        {
            NamedPipeClientStream pipe = new(".", IBridgeControlApi.Name, PipeDirection.InOut, PipeOptions.Asynchronous);
            await using (pipe.ConfigureAwait(false))
            {
                await pipe.ConnectAsync((int)ConnectTimeout.TotalMilliseconds, cancellationToken).ConfigureAwait(false);
                IBridgeControlApi server = JsonRpc.Attach<IBridgeControlApi>(pipe);
                BridgeServerStatus status = await server.GetStatusAsync().WaitAsync(ConnectTimeout, cancellationToken).ConfigureAwait(false);

                if (json)
                {
                    await Console.Out.WriteLineAsync(JsonSerializer.Serialize(status, JsonOptions)).ConfigureAwait(false);
                    return 0;
                }

                await Console.Out.WriteLineAsync("Server").ConfigureAwait(false);
                await Console.Out.WriteLineAsync("------").ConfigureAwait(false);
                await Console.Out.WriteLineAsync($"connectedClients  {status.ClientsCount}").ConfigureAwait(false);
                await Console.Out.WriteLineAsync($"shortcuts         {status.Shortcuts.Count}").ConfigureAwait(false);
                await Console.Out.WriteLineAsync().ConfigureAwait(false);

                await WriteProfilesAsync(status).ConfigureAwait(false);
                await WriteShortcutsAsync(status).ConfigureAwait(false);
                await WriteMouseAsync(status).ConfigureAwait(false);
                await WriteTeensyAsync(status).ConfigureAwait(false);
                await WriteControllerAsync(status).ConfigureAwait(false);
                await WriteSteamInputAsync(status).ConfigureAwait(false);
            }

            return 0;
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or ConnectionLostException)
        {
            await CliOutput.WriteErrorAsync($"server status: unavailable ({exception.Message})").ConfigureAwait(false);
            return 1;
        }
    }

    private static async Task WriteProfilesAsync(BridgeServerStatus status)
    {
        await Console.Out.WriteLineAsync("Profiles").ConfigureAwait(false);
        await Console.Out.WriteLineAsync("--------").ConfigureAwait(false);
        if (status.Profiles.Count == 0)
        {
            await Console.Out.WriteLineAsync("none").ConfigureAwait(false);
            await Console.Out.WriteLineAsync().ConfigureAwait(false);
            return;
        }

        foreach (BridgeProfileStatus profile in status.Profiles)
        {
            string state = profile.Active ? "active" : profile.ClientProcessId.HasValue ? "connected" : "idle";
            await Console.Out.WriteLineAsync($"{profile.Id}  {state}").ConfigureAwait(false);
            await Console.Out.WriteLineAsync($"  title       {profile.Definition.Title}").ConfigureAwait(false);
            await Console.Out.WriteLineAsync($"  clientPid   {FormatNumber(profile.ClientProcessId)}").ConfigureAwait(false);
            await Console.Out.WriteLineAsync($"  steamAppId  {FormatNumber(profile.EffectiveSteamAppId)}").ConfigureAwait(false);
            await Console.Out.WriteLineAsync($"  gamePids    {FormatList(profile.GameProcessIds)}").ConfigureAwait(false);
            await Console.Out.WriteLineAsync($"  mouse       {FormatText(profile.Definition.MouseOutput?.ToString())}").ConfigureAwait(false);
            await Console.Out.WriteLineAsync($"  controller  {FormatText(profile.Definition.ControllerOutput?.ToString())}").ConfigureAwait(false);
        }

        await Console.Out.WriteLineAsync().ConfigureAwait(false);
    }

    private static async Task WriteShortcutsAsync(BridgeServerStatus status)
    {
        await Console.Out.WriteLineAsync("Shortcuts").ConfigureAwait(false);
        await Console.Out.WriteLineAsync("---------").ConfigureAwait(false);
        if (status.Shortcuts.Count == 0)
        {
            await Console.Out.WriteLineAsync("none").ConfigureAwait(false);
            await Console.Out.WriteLineAsync().ConfigureAwait(false);
            return;
        }

        foreach (BridgeShortcutStatus shortcut in status.Shortcuts)
        {
            string pressed = FormatYesNo(shortcut.Pressed);
            await Console.Out.WriteLineAsync(
                    $"{shortcut.Keys}  {pressed}  {shortcut.Target}  {shortcut.Action}")
                .ConfigureAwait(false);
        }

        await Console.Out.WriteLineAsync().ConfigureAwait(false);
    }

    private static async Task WriteMouseAsync(BridgeServerStatus status)
    {
        await Console.Out.WriteLineAsync("Mouse").ConfigureAwait(false);
        await Console.Out.WriteLineAsync("-----").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"output      {status.Mouse.Output}").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"connected   {FormatYesNo(status.Mouse.OutputConnected)}").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"pointer     {FormatEnabled(status.Mouse.PointerEnabled)}").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"forwarding  {FormatActive(status.Mouse.Forwarding)}").ConfigureAwait(false);
        await Console.Out.WriteLineAsync().ConfigureAwait(false);
    }

    private static async Task WriteTeensyAsync(BridgeServerStatus status)
    {
        await Console.Out.WriteLineAsync("Teensy").ConfigureAwait(false);
        await Console.Out.WriteLineAsync("------").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"state       {status.Teensy.State}").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"configured  {FormatText(status.Teensy.ConfiguredPort)}").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"connected   {FormatText(status.Teensy.ConnectedPort)}").ConfigureAwait(false);
        await Console.Out.WriteLineAsync().ConfigureAwait(false);
    }

    private static async Task WriteControllerAsync(BridgeServerStatus status)
    {
        await Console.Out.WriteLineAsync("Controller").ConfigureAwait(false);
        await Console.Out.WriteLineAsync("----------").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"client             {status.Controller.Client}").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"steamControllers   {status.Controller.SteamControllers}").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"virtualControllers {status.Controller.VirtualControllers}").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"forwarding         {FormatActive(status.Controller.Forwarding)}").ConfigureAwait(false);
        await Console.Out.WriteLineAsync().ConfigureAwait(false);
    }

    private static async Task WriteSteamInputAsync(BridgeServerStatus status)
    {
        await Console.Out.WriteLineAsync("Steam Input").ConfigureAwait(false);
        await Console.Out.WriteLineAsync("-----------").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"profile    {FormatText(status.SteamInput.ProfileId)}").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"appId      {FormatNumber(status.SteamInput.AppId)}").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"lastError  {FormatText(status.SteamInput.LastError)}").ConfigureAwait(false);
    }

    private static string FormatNumber(int? value)
    {
        return value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "None";
    }

    private static string FormatNumber(uint? value)
    {
        return value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "None";
    }

    private static string FormatEnabled(bool value)
    {
        return value ? "Enabled" : "Disabled";
    }

    private static string FormatYesNo(bool value)
    {
        return value ? "Yes" : "No";
    }

    private static string FormatActive(bool value)
    {
        return value ? "Active" : "Inactive";
    }

    private static string FormatText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "None" : value;
    }

    private static string FormatList(IReadOnlyList<int> values)
    {
        return values.Count == 0 ? "None" : string.Join(", ", values);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        JsonSerializerOptions options = new() { WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
