using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using SteamInput;

internal static class SteamCommands
{
    // MARK: Commands
    // ========================================================================

    internal static Command CreateSteamCommand()
    {
        Command command = new("steam", "Inspect and control Steam Input.");
        command.Subcommands.Add(CreateListCommand());
        command.Subcommands.Add(CreateForceCommand());
        command.Subcommands.Add(CreateClearCommand());
        command.Subcommands.Add(CreateOpenConfigCommand());
        return command;
    }

    // MARK: Formatting
    // ========================================================================

    internal static string DisplayKind(SteamGameKind kind)
    {
        return kind switch
        {
            SteamGameKind.SteamApp => "steam",
            SteamGameKind.NonSteamShortcut => "shortcut",
            _ => kind.ToString(),
        };
    }

    internal static string DisplayPath(SteamGame game)
    {
        return game.LocalPath ?? string.Empty;
    }

    // MARK: Helpers
    // ========================================================================

    private static Command CreateListCommand()
    {
        Command command = new("list", "List Steam and non-Steam games known locally.");
        Option<string?> steamPathOption = CliOptions.CreateSteamPathOption();
        Option<uint?> userIdOption = CliOptions.CreateUserIdOption();
        command.Options.Add(steamPathOption);
        command.Options.Add(userIdOption);

        command.SetAction(async (parseResult, _) =>
        {
            IReadOnlyList<SteamGame> games = SteamInputClient.ListGames(
                parseResult.GetValue(steamPathOption),
                parseResult.GetValue(userIdOption));
            await PrintGamesAsync(games).ConfigureAwait(false);
        });

        return command;
    }

    private static Command CreateForceCommand()
    {
        Command command = new("force", "Force Steam Input to use a game configuration.");
        Argument<uint> appIdArgument = CliOptions.CreateAppIdArgument();
        command.Arguments.Add(appIdArgument);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            uint appId = parseResult.GetValue(appIdArgument);
            SteamInputClient client = new();
            await client.ForceAsync(appId, cancellationToken).ConfigureAwait(false);
            await Console.Out.WriteLineAsync($"forced appid={appId.ToString(CultureInfo.InvariantCulture)}").ConfigureAwait(false);
        });

        return command;
    }

    private static Command CreateClearCommand()
    {
        Command command = new("clear", "Clear Steam Input app id forcing.");
        command.SetAction(async (_, cancellationToken) =>
        {
            SteamInputClient client = new();
            await client.ClearAsync(cancellationToken).ConfigureAwait(false);
            await Console.Out.WriteLineAsync("cleared").ConfigureAwait(false);
        });

        return command;
    }

    private static Command CreateOpenConfigCommand()
    {
        Command command = new("open-config", "Open Steam's controller configurator for an app id.");
        Argument<uint> appIdArgument = CliOptions.CreateAppIdArgument();
        command.Arguments.Add(appIdArgument);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            uint appId = parseResult.GetValue(appIdArgument);
            SteamInputClient client = new();
            await client.OpenControllerConfigAsync(appId, cancellationToken).ConfigureAwait(false);
            await Console.Out.WriteLineAsync($"opened appid={appId.ToString(CultureInfo.InvariantCulture)}").ConfigureAwait(false);
        });

        return command;
    }

    private static async Task PrintGamesAsync(IReadOnlyList<SteamGame> games)
    {
        if (games.Count == 0)
        {
            await Console.Out.WriteLineAsync("no games found").ConfigureAwait(false);
            return;
        }

        int appIdWidth = Math.Max(5, games.Max(game => game.AppId.ToString(CultureInfo.InvariantCulture).Length));
        await Console.Out.WriteLineAsync(
            $"{Pad("appId", appIdWidth)}  {"kind",-8}  name  path")
            .ConfigureAwait(false);

        foreach (SteamGame game in games)
        {
            await Console.Out.WriteLineAsync(
                $"{Pad(game.AppId.ToString(CultureInfo.InvariantCulture), appIdWidth)}  " +
                $"{DisplayKind(game.Kind),-8}  {game.Name}  {DisplayPath(game)}")
                .ConfigureAwait(false);
        }
    }

    private static string Pad(string value, int width)
    {
        return value.PadLeft(width);
    }

}
