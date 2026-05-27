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
    private static readonly TimeSpan ReceiverPollInterval = TimeSpan.FromMilliseconds(100);

    public async Task WatchAsync(
        ClientRunState state,
        Func<IReadOnlyList<ObservedGameProcess>, CancellationToken, Task> update,
        CancellationToken cancellationToken)
    {
        LogReceiverWatch(state);

        // Receiver discovery is intentionally isolated here. Windows can raise
        // process start/exit events, but filtering arbitrary profile receiver
        // names reliably still requires checking the live process table.
        while (!cancellationToken.IsCancellationRequested)
        {
            IReadOnlyList<ObservedGameProcess> observed =
                GameProcessHost.FindReceivers(state.Launch.ReceiverProcesses);
            // For launched profiles, pre-existing receiver processes are not
            // this run. They must not keep the client alive or claim focus on
            // the server; only receivers that appear after launch are reported.
            IReadOnlyList<ObservedGameProcess> receivers = state.UpdateReceivers(observed);
            if (HasReceiverChange(state, receivers))
            {
                LogReceiverChange(state, receivers);
                await update(receivers, cancellationToken).ConfigureAwait(false);
            }

            state.SawReceiver |= receivers.Count != 0;
            if (state.SawReceiver && receivers.Count == 0)
            {
                return;
            }

            await Task.Delay(ReceiverPollInterval, cancellationToken).ConfigureAwait(false);
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

    private static bool HasReceiverChange(
        ClientRunState state,
        IReadOnlyList<ObservedGameProcess> observed)
    {
        string signature = string.Join(
            ",",
            observed.OrderBy(process => process.ProcessId).Select(process => process.ProcessId));
        if (signature == state.LastObservedSignature)
        {
            return false;
        }

        state.LastObservedSignature = signature;
        return true;
    }

    private void LogReceiverChange(
        ClientRunState state,
        IReadOnlyList<ObservedGameProcess> observed)
    {
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
