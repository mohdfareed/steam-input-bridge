using System.CommandLine;
using System.Threading.Tasks;
using SteamInputBridge.App.Tray;

namespace SteamInputBridge.App.Commands;

internal static class AppMode
{
    public static Task<int> RunAsync(string[] args)
    {
        return CreateRootCommand().Parse(args).InvokeAsync();
    }

    private static RootCommand CreateRootCommand()
    {
        RootCommand root = new("Steam Input Bridge");
        root.Subcommands.Add(CreateTrayCommand());
        root.Subcommands.Add(ShortcutCommands.CreateCommand());
        return root;
    }

    private static Command CreateTrayCommand()
    {
        Command tray = new("tray", "Run the tray application.");
        tray.SetAction((_, _) => Task.FromResult(TrayMode.Run()));
        return tray;
    }
}
