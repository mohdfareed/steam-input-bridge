using System;
using SteamInputBridge.Cli.Commands;
using SteamInputBridge.Cli.Host;

try
{
    return await CliMode.RunAsync(args).ConfigureAwait(false);
}
catch (Exception exception) when (exception is not OperationCanceledException)
{
    await CliOutput.WriteErrorAsync(exception.Message).ConfigureAwait(false);
    return 1;
}
