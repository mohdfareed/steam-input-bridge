using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SteamInputBridge.Hosting.Client.Connection;
using SteamInputBridge.Hosting.Server.Orchestration;
using SteamInputBridge.Steam;
using AppHostSetup = SteamInputBridge.App.AppSetup;

namespace SteamInputBridge.App.Cli;

internal static class SteamCommands
{
    public static Command Create()
    {
        Command steam = new("steam", "Inspect and control Steam Input.");
        steam.Subcommands.Add(CreateListCommand());
        steam.Subcommands.Add(CreateForceCommand());
        steam.Subcommands.Add(CreateClearCommand());
        steam.Subcommands.Add(CreateOpenConfigCommand());
        steam.Subcommands.Add(CreateStatusCommand());
        steam.Subcommands.Add(CreateSrmCommand());
        return steam;
    }

    // MARK: Commands
    // ========================================================================

    private static Command CreateListCommand()
    {
        Command command = new("list", "List Steam and non-Steam games known locally.");
        Option<string?> steamPath = new("--steam-path")
        {
            Description = "Steam install path.",
        };
        Option<uint?> userId = new("--user-id")
        {
            Description = "Steam userdata id for non-Steam shortcuts.",
        };

        command.Options.Add(steamPath);
        command.Options.Add(userId);

        command.SetAction(async (parseResult, _) =>
        {
            using IHost app = AppHostSetup.CreateCli();

            IReadOnlyList<SteamGame> games = SteamInputClient.ListGames(
                parseResult.GetValue(steamPath),
                parseResult.GetValue(userId));

            await PrintGamesAsync(games).ConfigureAwait(false);
        });

        return command;
    }

    private static Command CreateForceCommand()
    {
        Command command = new("force", "Force Steam Input to use an app or desktop configuration.");
        Argument<string> appId = CreateAppIdArgument("app-id", allowDesktop: true);

        command.Arguments.Add(appId);
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            uint parsedAppId = ParseAppId(parseResult.GetValue(appId));
            SteamInputClient client = new();
            await client.ForceConfigAsync(parsedAppId, cancellationToken).ConfigureAwait(false);
            await Console.Out.WriteLineAsync($"forced appid={parsedAppId.ToString(CultureInfo.InvariantCulture)}")
                .ConfigureAwait(false);
        });

        return command;
    }

    private static Command CreateClearCommand()
    {
        Command command = new("clear", "Clear Steam Input app id forcing.");

        command.SetAction(async (_, cancellationToken) =>
        {
            SteamInputClient client = new();
            await client.ForceConfigAsync(null, cancellationToken).ConfigureAwait(false);
            await Console.Out.WriteLineAsync("cleared").ConfigureAwait(false);
        });

        return command;
    }

    private static Command CreateOpenConfigCommand()
    {
        Command command = new("open-config", "Open Steam's controller configurator.");
        Argument<string> appId = CreateAppIdArgument("app-id", allowDesktop: true);

        command.Arguments.Add(appId);
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            uint parsedAppId = ParseAppId(parseResult.GetValue(appId));
            SteamInputClient client = new();
            await client.OpenControllerConfigAsync(parsedAppId, cancellationToken).ConfigureAwait(false);
            await Console.Out.WriteLineAsync($"opened appid={parsedAppId.ToString(CultureInfo.InvariantCulture)}")
                .ConfigureAwait(false);
        });

        return command;
    }

    private static Command CreateStatusCommand()
    {
        Command command = new("status", "Show Steam Input forcing tracked by the running server.");

        command.SetAction(async (_, cancellationToken) =>
        {
            using IHost app = AppHostSetup.CreateCli();
            ClientService client = app.Services.GetRequiredService<ClientService>();
            await using (client.ConfigureAwait(false))
            {
                await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
                ServerStatus status = await client.GetStatusAsync(cancellationToken).ConfigureAwait(false);
                ServerSteamInputStatus steamInput = status.SteamInput;
                string name = steamInput.AppId is uint appId ? ResolveGameName(appId) : "none";

                await Console.Out.WriteLineAsync(
                        $"forced={(steamInput.Forced ? "true" : "false")} " +
                        $"appId={FormatAppId(steamInput.AppId)} name={name} " +
                        $"client={steamInput.ClientId?.ToString() ?? "none"} " +
                        $"error={steamInput.LastError ?? "none"}")
                    .ConfigureAwait(false);
            }
        });

        return command;
    }

    private static Command CreateSrmCommand()
    {
        Command command = new("export", "Export configured profiles to SRM manifest.");
        Argument<string?> path = new("path")
        {
            Arity = ArgumentArity.ZeroOrOne,
            Description = "Path to the SRM manifest file. Overrides configured path.",
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            using IHost app = AppHostSetup.CreateCli();
            SrmExportResult result = SrmExport.Export(app.Services, parseResult.GetValue(path));
            if (!result.Exported)
            {
                throw new InvalidOperationException(result.Error ?? "Could not export SRM manifest.");
            }

            await Console.Out.WriteLineAsync($"manifest={result.ManifestPath} profiles={result.ProfileCount}")
                .ConfigureAwait(false);
        });

        return command;
    }

    // MARK: Commands
    // ========================================================================

    private static Argument<string> CreateAppIdArgument(string name, bool allowDesktop)
    {
        Argument<string> argument = new(name)
        {
            DefaultValueFactory = (_) => allowDesktop ? "desktop" : string.Empty,
            Description = allowDesktop
                ? "Steam app id, non-Steam shortcut app id, or desktop."
                : "Steam app id or non-Steam shortcut app id.",
        };

        argument.Validators.Add(result =>
        {
            string? value = result.GetValue(argument);
            if (allowDesktop && string.Equals(value, "desktop", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint appId) ||
                appId == 0)
            {
                result.AddError($"{name} must be a positive app id or desktop.");
            }
        });

        return argument;
    }

    private static uint ParseAppId(string? value)
    {
        return string.Equals(value, "desktop", StringComparison.OrdinalIgnoreCase)
            ? SteamInputClient.DesktopConfigAppId
            : uint.Parse(value ?? string.Empty, NumberStyles.Integer, CultureInfo.InvariantCulture);
    }

    private static string FormatAppId(uint? appId)
    {
        return appId.HasValue
            ? appId.Value.ToString(CultureInfo.InvariantCulture)
            : "none";
    }

    private static string ResolveGameName(uint appId)
    {
        try
        {
            foreach (SteamGame game in SteamInputClient.ListGames())
            {
                if (game.AppId == appId)
                {
                    return game.Name;
                }
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
        }

        return appId == SteamInputClient.DesktopConfigAppId ? "Desktop" : "unknown";
    }

    private static async Task PrintGamesAsync(IReadOnlyList<SteamGame> games)
    {
        if (games.Count == 0)
        {
            await Console.Out.WriteLineAsync("no games found").ConfigureAwait(false);
            return;
        }

        static string DisplayKind(SteamGameKind kind)
        {
            return kind switch
            {
                SteamGameKind.SteamApp => "steam",
                SteamGameKind.NonSteamShortcut => "shortcut",
                _ => kind.ToString(),
            };
        }

        static string Pad(string value, int width)
        {
            return value.PadLeft(width);
        }

        int appIdWidth = Math.Max(5, games.Max(game => game.AppId.ToString(CultureInfo.InvariantCulture).Length));
        await Console.Out.WriteLineAsync($"{Pad("appId", appIdWidth)}  {"kind",-8}  name  path").ConfigureAwait(false);
        foreach (SteamGame game in games)
        {
            await Console.Out.WriteLineAsync(
                    $"{Pad(game.AppId.ToString(CultureInfo.InvariantCulture), appIdWidth)}  " +
                    $"{DisplayKind(game.Kind),-8}  {game.Name}  {game.LocalPath ?? string.Empty}")
                .ConfigureAwait(false);
        }
    }
}
