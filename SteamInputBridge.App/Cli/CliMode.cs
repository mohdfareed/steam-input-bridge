using System.CommandLine;
using System.Threading.Tasks;

namespace SteamInputBridge.App.Cli;

internal static class CliMode
{
    public static Task<int> RunAsync(string[] args)
    {
        RootCommand root = new("Steam Input Bridge");
        root.Subcommands.Add(Commands.CreateServerCommand());
        root.Subcommands.Add(Commands.CreateClientCommand());
        root.Subcommands.Add(Commands.CreateShortcutCommand());
        root.Subcommands.Add(SteamCommands.Create());

        return root.Parse(args).InvokeAsync();
    }
}
