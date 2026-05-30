using System;
using SteamInputBridge.App.Cli;

namespace SteamInputBridge.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            return CliMode.RunAsync(args).GetAwaiter().GetResult();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Console.Error.WriteLine($"Unhandled exception: {exception}");
            return 1;
        }
    }
}
