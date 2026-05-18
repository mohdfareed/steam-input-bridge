using System.CommandLine;
using Refactor.Cli;

RootCommand root = new("app refactored");
root.Subcommands.Add(Commands.CreateServerCommand());
root.Subcommands.Add(Commands.CreateClientCommand());
root.Subcommands.Add(SteamCommands.Create());

return await root.Parse(args).InvokeAsync();
