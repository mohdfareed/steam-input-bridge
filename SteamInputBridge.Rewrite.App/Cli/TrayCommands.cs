using System.CommandLine;
using System.Threading.Tasks;
using SteamInputBridge.App.Tray;

namespace SteamInputBridge.App.Cli;

internal static class TrayCommands
{
    public static Command CreateCommand()
    {
        Command tray = new("tray", "Run the tray application.");
        tray.SetAction((_, _) => Task.FromResult(TrayMode.Run()));
        return tray;
    }
}
