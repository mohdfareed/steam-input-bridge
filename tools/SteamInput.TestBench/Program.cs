using System;
using System.Diagnostics;
using System.Threading.Tasks;

internal static class Program
{
    // MARK: Entry
    // ========================================================================

    private static async Task<int> Main()
    {
        await Console.Out.WriteLineAsync("steam input testbench").ConfigureAwait(false);
        await Console.Out.WriteLineAsync("type 'help' for commands, 'exit' to quit.").ConfigureAwait(false);
        await Console.Out.WriteLineAsync().ConfigureAwait(false);

        while (true)
        {
            await Console.Out.WriteAsync("steam> ").ConfigureAwait(false);
            string? input = await Console.In.ReadLineAsync().ConfigureAwait(false);
            if (input is null)
            {
                return 0;
            }

            string command = input.Trim();
            if (command.Length == 0)
            {
                continue;
            }

            if (IsExit(command))
            {
                return 0;
            }

            await RunCommandAsync(command).ConfigureAwait(false);
        }
    }

    // MARK: Commands
    // ========================================================================

    private static async Task RunCommandAsync(string command)
    {
        if (command.Equals("help", StringComparison.OrdinalIgnoreCase))
        {
            await PrintHelpAsync().ConfigureAwait(false);
            return;
        }

        if (command.Equals("hello", StringComparison.OrdinalIgnoreCase))
        {
            await Console.Out.WriteLineAsync("hello from the Steam Input testbench").ConfigureAwait(false);
            return;
        }

        if (command.Equals("launch", StringComparison.OrdinalIgnoreCase))
        {
            await PrintLaunchInfoAsync().ConfigureAwait(false);
            return;
        }

        await Console.Out.WriteLineAsync($"unknown command: {command}").ConfigureAwait(false);
    }

    private static async Task PrintHelpAsync()
    {
        await Console.Out.WriteLineAsync("commands").ConfigureAwait(false);
        await Console.Out.WriteLineAsync("  hello   prints a simple launch sanity check").ConfigureAwait(false);
        await Console.Out.WriteLineAsync("  launch  prints process and Steam-related environment info").ConfigureAwait(false);
        await Console.Out.WriteLineAsync("  exit    quits").ConfigureAwait(false);
    }

    private static async Task PrintLaunchInfoAsync()
    {
        using Process process = Process.GetCurrentProcess();
        await Console.Out.WriteLineAsync($"process     {process.ProcessName} ({process.Id})").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"cwd         {Environment.CurrentDirectory}").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"base        {AppContext.BaseDirectory}").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"SteamAppId  {DisplayEnvironmentValue("SteamAppId")}").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"SteamGameId {DisplayEnvironmentValue("SteamGameId")}").ConfigureAwait(false);
    }

    private static bool IsExit(string command)
    {
        return command.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
            command.Equals("quit", StringComparison.OrdinalIgnoreCase);
    }

    private static string DisplayEnvironmentValue(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? "(not set)" : value;
    }
}
