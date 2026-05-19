using System.CommandLine;

namespace VirtualMouse.Cli;

internal static class CliMode
{
    public static Task<int> RunAsync(string[] args)
    {
        RootCommand root = new("Virtual Mouse");
        root.Subcommands.Add(Commands.CreateServerCommand());
        root.Subcommands.Add(Commands.CreateClientCommand());
        root.Subcommands.Add(SteamCommands.Create());

        return root.Parse(args).InvokeAsync();
    }
}
