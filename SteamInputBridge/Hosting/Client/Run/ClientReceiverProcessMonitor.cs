using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Runtime;

namespace SteamInputBridge.Hosting.Client.Run;

internal sealed class ClientReceiverProcessMonitor(ILogger logger)
{
    public async Task WatchAsync(
        ClientRunState state,
        Func<IReadOnlyList<ObservedGameProcess>, CancellationToken, Task> update,
        CancellationToken cancellationToken)
    {
        LogReceiverWatch(state);

        while (!cancellationToken.IsCancellationRequested)
        {
            IReadOnlyList<ObservedGameProcess> observed =
                GameProcessHost.FindReceivers(state.Launch.ReceiverProcesses);
            LogReceiverChange(state, observed);
            await update(observed, cancellationToken).ConfigureAwait(false);

            state.SawReceiver |= observed.Count != 0;
            if (state.SawReceiver && observed.Count == 0)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
        }
    }

    private void LogReceiverWatch(ClientRunState state)
    {
        if (!logger.IsEnabled(LogLevel.Information))
        {
            return;
        }

        string receivers = string.Join(", ", state.Launch.ReceiverProcesses);
        HostingLog.WatchingReceiverProcesses(logger, state.Launch.ProfileId, receivers);
    }

    private void LogReceiverChange(
        ClientRunState state,
        IReadOnlyList<ObservedGameProcess> observed)
    {
        string signature = string.Join(
            ",",
            observed.OrderBy(process => process.ProcessId).Select(process => process.ProcessId));
        if (signature == state.LastObservedSignature)
        {
            return;
        }

        state.LastObservedSignature = signature;
        HostingLog.ReceiverProcesses(
            logger,
            state.Launch.ProfileId,
            observed.Count,
            observed.Count == 0 ? "none" : FormatProcesses(observed));
    }

    private static string FormatProcesses(IReadOnlyList<ObservedGameProcess> processes)
    {
        return string.Join(
            ", ",
            processes
                .OrderBy(process => process.ProcessId)
                .Select(process => $"{process.ProcessName}:{process.ProcessId}"));
    }
}
