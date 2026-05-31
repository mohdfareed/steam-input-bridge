using System;
using SteamInputBridge.Cli.Commands;

try
{
    return await CliMode.RunAsync(args).ConfigureAwait(false);
}
catch (Exception exception) when (exception is not OperationCanceledException)
{
    await Console.Error.WriteLineAsync(exception.Message).ConfigureAwait(false);
    await Console.Error.WriteLineAsync(exception.ToString()).ConfigureAwait(false);
    return 1;
}
