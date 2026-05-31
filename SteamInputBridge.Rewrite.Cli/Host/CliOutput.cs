using System;
using System.Threading.Tasks;

namespace SteamInputBridge.Cli.Host;

internal static class CliOutput
{
    public static async Task WriteErrorAsync(string message)
    {
        if (Console.IsErrorRedirected)
        {
            await Console.Error.WriteLineAsync(message).ConfigureAwait(false);
            return;
        }

        ConsoleColor previousColor = Console.ForegroundColor;
        try
        {
            Console.ForegroundColor = ConsoleColor.Red;
            await Console.Error.WriteLineAsync(message).ConfigureAwait(false);
        }
        finally
        {
            Console.ForegroundColor = previousColor;
        }
    }
}
