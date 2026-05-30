using System;
using System.CommandLine;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SteamInputBridge.App.Hosting;
using SteamInputBridge.Settings;
using SteamInputBridge.Steam;

namespace SteamInputBridge.App.Cli;

internal static class SrmCommands
{
    public static Command CreateCommand()
    {
        Command export = new("export", "Export configured profiles to a Steam ROM Manager manifest.");
        export.Arguments.Add(new Argument<string?>("path")
        {
            Arity = ArgumentArity.ZeroOrOne,
            Description = "Manifest path. Overrides Steam:SrmExportPath.",
        });
        export.SetAction((parseResult, _) =>
        {
            SrmExportResult result = Export(parseResult.GetValue<string?>("path"));
            Console.WriteLine($"manifest={result.ManifestPath} profiles={result.ProfileCount}");
            return Task.CompletedTask;
        });

        Command srm = new("srm", "Manage Steam ROM Manager export files.");
        srm.Subcommands.Add(export);
        return srm;
    }

    private static SrmExportResult Export(string? manifestPathOverride = null)
    {
        using IHost host = AppHost.CreateCli();
        SettingsService settings = host.Services.GetRequiredService<SettingsService>();
        SettingsFile settingsFile = host.Services.GetRequiredService<SettingsFile>();

        SteamInputBridgeSettings current = settings.Current;
        string manifestPath = ResolveManifestPath(manifestPathOverride ?? current.Steam.SrmExportPath, settingsFile.Path);
        string shortcutPath = Path.Combine(AppContext.BaseDirectory, "SteamInputBridge.exe");
        string manifest = SteamRomManagerExport.CreateJson(current.Games, shortcutPath);

        string? directory = Path.GetDirectoryName(manifestPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            _ = Directory.CreateDirectory(directory);
        }

        File.WriteAllText(manifestPath, manifest);
        return new SrmExportResult(manifestPath, current.Games.Count);
    }

    private static string ResolveManifestPath(string? path, string settingsPath)
    {
        string filePath = Environment.ExpandEnvironmentVariables(string.IsNullOrWhiteSpace(path) ? "srm-manifest.json" : path);
        return Path.IsPathFullyQualified(filePath)
            ? filePath
            : Path.Combine(Path.GetDirectoryName(settingsPath) ?? AppContext.BaseDirectory, filePath);
    }

    private sealed record SrmExportResult(string ManifestPath, int ProfileCount);
}
