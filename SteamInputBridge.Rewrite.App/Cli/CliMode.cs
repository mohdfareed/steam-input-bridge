using System.CommandLine;
using System.Threading.Tasks;

namespace SteamInputBridge.App.Cli;

internal static class CliMode
{
    public static Task<int> RunAsync(string[] args)
    {
        return CreateRootCommand().Parse(args).InvokeAsync();
    }

    internal static RootCommand CreateRootCommand()
    {
        RootCommand root = new("Steam Input Bridge");
        root.Subcommands.Add(ServerCommands.CreateCommand());
        root.Subcommands.Add(ClientCommands.CreateCommand());
        root.Subcommands.Add(TrayCommands.CreateCommand());
        root.Subcommands.Add(ShortcutCommands.CreateCommand());
        root.Subcommands.Add(SteamCommands.CreateCommand());
        return root;
    }
}
