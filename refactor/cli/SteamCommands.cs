using System.CommandLine;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using VirtualMouse.Settings;
using VirtualMouse.Settings.Profiles;
using VirtualMouse.Steam;

namespace Refactor.Cli;

internal static class SteamCommands
{
    public static Command Create()
    {
        Command steam = new("steam", "Inspect and control Steam Input.");
        steam.Subcommands.Add(CreateListCommand());
        steam.Subcommands.Add(CreateForceCommand());
        steam.Subcommands.Add(CreateClearCommand());
        steam.Subcommands.Add(CreateOpenConfigCommand());
        steam.Subcommands.Add(CreateSrmCommand());
        return steam;
    }

    internal static string DisplayKind(SteamGameKind kind)
    {
        return kind switch
        {
            SteamGameKind.SteamApp => "steam",
            SteamGameKind.NonSteamShortcut => "shortcut",
            _ => kind.ToString(),
        };
    }

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

    private static Command CreateSrmCommand()
    {
        Command command = new("srm", "Export Steam ROM Manager manifests.");
        Command export = new("export", "Export configured profiles.");
        export.SetAction(async (_, _) =>
        {
            using IHost app = AppSetup.Create();
            ProfilesService profiles = app.Services.GetRequiredService<ProfilesService>();
            SteamSettings steam = app.Services.GetRequiredService<IOptions<SteamSettings>>().Value;
            string manifestPath = ResolveManifestPath(steam.RomManager.ManifestPath);
            string executablePath = Environment.ProcessPath ??
                throw new InvalidOperationException("Could not resolve executable path.");

            SteamRomManagerExport.Write(profiles, executablePath, manifestPath);
            await Console.Out.WriteLineAsync($"srm manifest={manifestPath} profiles={profiles.ListProfileIds().Count}")
                .ConfigureAwait(false);
        });
        command.Subcommands.Add(export);
        return command;
    }

    private static Argument<string> CreateAppIdArgument(string name, bool allowDesktop)
    {
        Argument<string> argument = new(name)
        {
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

    private static string ResolveManifestPath(string? configuredPath)
    {
        string path = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(AppContext.BaseDirectory, "srm-manifest.json")
            : Environment.ExpandEnvironmentVariables(configuredPath);
        return Path.IsPathFullyQualified(path)
            ? path
            : Path.Combine(AppContext.BaseDirectory, path);
    }

    private static async Task PrintGamesAsync(IReadOnlyList<SteamGame> games)
    {
        if (games.Count == 0)
        {
            await Console.Out.WriteLineAsync("no games found").ConfigureAwait(false);
            return;
        }

        int appIdWidth = Math.Max(5, games.Max(game => game.AppId.ToString(CultureInfo.InvariantCulture).Length));
        await Console.Out.WriteLineAsync($"{Pad("appId", appIdWidth)}  {"kind",-8}  name  path")
            .ConfigureAwait(false);

        foreach (SteamGame game in games)
        {
            await Console.Out.WriteLineAsync(
                    $"{Pad(game.AppId.ToString(CultureInfo.InvariantCulture), appIdWidth)}  " +
                    $"{DisplayKind(game.Kind),-8}  {game.Name}  {game.LocalPath ?? string.Empty}")
                .ConfigureAwait(false);
        }
    }

    private static string Pad(string value, int width)
    {
        return value.PadLeft(width);
    }
}
