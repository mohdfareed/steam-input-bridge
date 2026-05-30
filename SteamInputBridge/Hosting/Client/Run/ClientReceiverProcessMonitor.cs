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
        CancellationToken cancellationToken)
    {
        ClientRunLaunch launch = state.RegisteredLaunch;
        LogReceiverWatch(launch);

        // Receiver discovery is intentionally isolated here. Windows can raise
        // process start/exit events, but filtering arbitrary profile receiver
        // names reliably still requires checking the live process table.
        while (!cancellationToken.IsCancellationRequested)
        {
            IReadOnlyList<ObservedGameProcess> observed =
                GameProcessHost.FindReceivers(launch.ReceiverProcesses);
            // For launched profiles, pre-existing receiver processes are not
            // this run. They must not keep the client alive or claim focus on
            // the server; only receivers that appear after launch are reported.
            IReadOnlyList<ObservedGameProcess> receivers = state.UpdateReceivers(observed);
            if (HasReceiverChange(state, receivers))
            {
                LogReceiverChange(launch, receivers);
            }

            state.SawReceiver |= receivers.Count != 0;
            if (state.SawReceiver && receivers.Count == 0)
            {
                return;
            }

            await Task.Delay(ReceiverPollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private void LogReceiverWatch(ClientRunLaunch launch)
    {
        if (!logger.IsEnabled(LogLevel.Information))
        {
            return;
        }

        string receivers = string.Join(", ", launch.ReceiverProcesses);
        HostingLog.WatchingReceiverProcesses(logger, launch.ProfileId, receivers);
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
        ClientRunLaunch launch,
        IReadOnlyList<ObservedGameProcess> observed)
    {
        HostingLog.ReceiverProcesses(
            logger,
            launch.ProfileId,
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
