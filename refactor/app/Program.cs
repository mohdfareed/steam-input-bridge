using VirtualMouse.Cli;
using VirtualMouse.Shortcut;
using VirtualMouse.Tray;

namespace VirtualMouse.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        return RunAsync(args).GetAwaiter().GetResult();
    }

    private static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || IsMode(args[0], "tray"))
        {
            return TrayMode.Run();
        }

        if (IsMode(args[0], "shortcut"))
        {
            return await ShortcutMode.RunAsync(args[1..]).ConfigureAwait(false);
        }

        WindowsConsole.AttachForCli();
        return await CliMode.RunAsync(args).ConfigureAwait(false);
    }

    private static bool IsMode(string value, string mode)
    {
        return string.Equals(value, mode, StringComparison.OrdinalIgnoreCase);
    }
}
