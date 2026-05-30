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

        // Hosting
        root.Subcommands.Add(Commands.CreateServerCommand());
        root.Subcommands.Add(Commands.CreateClientCommand());
        root.Subcommands.Add(Commands.CreateShortcutCommand());
        root.Subcommands.Add(Commands.CreateTrayCommand());

        // Tooling
        root.Subcommands.Add(SteamCommands.CreateCommand());
        root.Subcommands.Add(SrmCommands.CreateCommand());

        return root;
    }
}
