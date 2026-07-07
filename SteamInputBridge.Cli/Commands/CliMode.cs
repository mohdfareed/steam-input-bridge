using System.CommandLine;
using System.Threading.Tasks;

namespace SteamInputBridge.Cli.Commands;

internal static class CliMode
{
    public static Task<int> RunAsync(string[] args)
    {
        return CreateRootCommand().Parse(args).InvokeAsync();
    }

    private static RootCommand CreateRootCommand()
    {
        RootCommand root = new("Steam Input Bridge CLI");
        root.Subcommands.Add(ServerCommands.CreateCommand());
        root.Subcommands.Add(ClientCommands.CreateCommand());
        root.Subcommands.Add(DiagnosticsCommands.CreateCommand());
        root.Subcommands.Add(SteamCommands.CreateCommand());
        return root;
    }
}
