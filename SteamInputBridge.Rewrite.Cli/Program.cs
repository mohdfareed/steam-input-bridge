using System;
using SteamInputBridge.Cli;
using SteamInputBridge.Cli.Commands;

try
{
    return await CliMode.RunAsync(args).ConfigureAwait(false);
}
catch (Exception exception) when (exception is not OperationCanceledException)
{
    await ConsoleOutput.WriteErrorAsync(exception.Message).ConfigureAwait(false);
    return 1;
}
