using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SteamInputBridge.Settings;
using SteamInputBridge.Steam;

namespace SteamInputBridge.Cli.Commands;

internal static class SteamCommands
{
    public static Command CreateCommand()
    {
        Command steam = new("steam", "Inspect and control Steam Input.");
        steam.Subcommands.Add(CreateExportCommand());
        steam.Subcommands.Add(CreateListCommand());
        steam.Subcommands.Add(CreateForceCommand());
        steam.Subcommands.Add(CreateClearCommand());
        steam.Subcommands.Add(CreateOpenConfigCommand());
        return steam;
    }

    // MARK: Commands
    // ========================================================================

    private static Command CreateExportCommand()
    {
        Command export = new("export", "Export configured profiles to a Steam ROM Manager manifest.");
        export.Arguments.Add(new Argument<string?>("path")
        {
            Arity = ArgumentArity.ZeroOrOne,
            Description = "Manifest path. Overrides Steam:SrmExportPath.",
        });
        export.SetAction((parseResult, _) =>
        {
            string manifestPath = Export(parseResult.GetValue<string?>("path"));
            Console.WriteLine($"manifest={manifestPath}");
            return Task.CompletedTask;
        });

        return export;
    }

    private static Command CreateListCommand()
    {
        Command command = new("list", "List Steam and non-Steam games known locally.");
        Option<string?> steamPath = new("--steam-path") { Description = "Steam install path." };
        Option<uint?> userId = new("--user-id") { Description = "Steam userdata id for non-Steam shortcuts." };

        command.Options.Add(steamPath);
        command.Options.Add(userId);
        command.SetAction(async (parseResult, _) =>
        {
            IReadOnlyList<SteamGame> games = SteamInputClient.ListGames(parseResult.GetValue(steamPath), parseResult.GetValue(userId));
            await PrintGamesAsync(games).ConfigureAwait(false);
        });

        return command;
    }

    private static Command CreateForceCommand()
    {
        Command command = new("force", "Force Steam Input to use an app configuration.");
        Argument<string> appId = CreateAppIdArgument("app-id");

        command.Arguments.Add(appId);
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            uint parsedAppId = ParseAppId(parseResult.GetValue(appId));
            await new SteamInputClient().ForceConfigAsync(parsedAppId, cancellationToken).ConfigureAwait(false);
            await Console.Out.WriteLineAsync($"forced appid={parsedAppId.ToString(CultureInfo.InvariantCulture)}").ConfigureAwait(false);
        });

        return command;
    }

    private static Command CreateClearCommand()
    {
        Command command = new("clear", "Clear Steam Input app id forcing.");
        command.SetAction(async (_, cancellationToken) =>
        {
            await new SteamInputClient().ForceConfigAsync(null, cancellationToken).ConfigureAwait(false);
            await Console.Out.WriteLineAsync("cleared").ConfigureAwait(false);
        });

        return command;
    }

    private static Command CreateOpenConfigCommand()
    {
        Command command = new("open-config", "Open Steam's controller configurator.");
        Argument<string> appId = CreateAppIdArgument("app-id");

        command.Arguments.Add(appId);
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            uint parsedAppId = ParseAppId(parseResult.GetValue(appId));
            await new SteamInputClient().OpenSteamConfigAsync(parsedAppId, cancellationToken).ConfigureAwait(false);
            await Console.Out.WriteLineAsync($"opened appid={parsedAppId.ToString(CultureInfo.InvariantCulture)}").ConfigureAwait(false);
        });

        return command;
    }

    private static Argument<string> CreateAppIdArgument(string name)
    {
        Argument<string> argument = new(name)
        {
            DefaultValueFactory = (_) => SteamInputClient.DesktopConfigAppId.ToString(CultureInfo.InvariantCulture),
            Description = "Steam app id or non-Steam shortcut app id.",
        };

        argument.Validators.Add(result =>
        {
            string? value = result.GetValue(argument);
            if (!uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint appId) || appId == 0)
            {
                result.AddError($"{name} must be a positive app id.");
            }
        });

        return argument;
    }

    // MARK: Implementation
    // ========================================================================

    private static string Export(string? manifestPathOverride = null)
    {
        using IHost host = CliHost.CreateCli();
        SettingsService settings = host.Services.GetRequiredService<SettingsService>();
        SettingsFile settingsFile = host.Services.GetRequiredService<SettingsFile>();
        string shortcutPath = System.IO.Path.Combine(AppContext.BaseDirectory, "SteamInputBridge.exe");
        return SteamRomManagerExport.WriteManifest(settings.Current, settingsFile.Path, shortcutPath, manifestPathOverride);
    }

    private static uint ParseAppId(string? value)
    {
        return uint.Parse(value ?? string.Empty, NumberStyles.Integer, CultureInfo.InvariantCulture);
    }

    private static async Task PrintGamesAsync(IReadOnlyList<SteamGame> games)
    {
        if (games.Count == 0)
        {
            await Console.Out.WriteLineAsync("no games found").ConfigureAwait(false);
            return;
        }

        int appIdWidth = Math.Max(5, games.Max(game => game.AppId.ToString(CultureInfo.InvariantCulture).Length));
        await Console.Out.WriteLineAsync($"{Pad("appId", appIdWidth)}  {"kind",-8}  name  path").ConfigureAwait(false);

        foreach (SteamGame game in games)
        {
            await Console.Out.WriteLineAsync($"{Pad(game.AppId.ToString(CultureInfo.InvariantCulture), appIdWidth)}  {DisplayKind(game.Kind),-8}  {game.Name}  {game.LocalPath ?? string.Empty}").ConfigureAwait(false);
        }
    }

    private static string DisplayKind(SteamGameKind kind)
    {
        return kind switch
        {
            SteamGameKind.SteamApp => "steam",
            SteamGameKind.NonSteamShortcut => "shortcut",
            _ => kind.ToString(),
        };
    }

    private static string Pad(string value, int width)
    {
        return value.PadLeft(width);
    }
}
