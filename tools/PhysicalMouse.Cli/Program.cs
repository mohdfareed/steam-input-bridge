using System.CommandLine;
using System.Threading.Tasks;

internal static class Program
{
    // MARK: Entry
    // ========================================================================

    private static Task<int> Main(string[] args)
    {
        RootCommand root = new("CLI for VIIPER tests.");
        root.Subcommands.Add(CliBasicCommands.CreateConnectCommand());
        root.Subcommands.Add(CliBasicCommands.CreateMoveCommand());
        root.Subcommands.Add(CliBasicCommands.CreateClickCommand());
        root.Subcommands.Add(CliBasicCommands.CreateWheelCommand());
        root.Subcommands.Add(CliDiagnosticsCommands.CreateSmokeCommand());
        root.Subcommands.Add(CliDiagnosticsCommands.CreateBenchCommand());
        root.Subcommands.Add(CliDiagnosticsCommands.CreateSmoothCommand());
        return root.Parse(args).InvokeAsync();
    }
}
