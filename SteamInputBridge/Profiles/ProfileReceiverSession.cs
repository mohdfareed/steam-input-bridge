using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Hosting;
using SteamInputBridge.Settings;

namespace SteamInputBridge.Profiles;

internal sealed class ProfileReceiverSession : IDisposable
{
    private static readonly TimeSpan ReceiverPollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ReceiverCloseTimeout = TimeSpan.FromSeconds(5);

    private readonly GameProfile _definition;
    private readonly IBridgeClientApi _control;
    private readonly Func<int, ReceiverWindowActivationResult> _activateReceiver;
    private readonly Func<IReadOnlyList<string>, HashSet<int>> _findReceivers;
    private readonly ILogger<ProfileClientsService> _logger;
    private readonly Action _receiversChanged;
    private readonly CancellationTokenSource _stop;
    private readonly bool _startExecutable;
    private readonly Lock _gate = new();
    private readonly HashSet<int> _receivers;
    private readonly HashSet<int> _activationCompleted = [];
    private readonly HashSet<int> _windowNotFoundLogged = [];
    private bool _receiverSeen;
    private bool _clientStopRequested;

    public ProfileReceiverSession(
        string profileId,
        GameProfile definition,
        IBridgeClientApi control,
        Func<int, ReceiverWindowActivationResult> activateReceiver,
        ILogger<ProfileClientsService> logger,
        Action receiversChanged,
        Func<IReadOnlyList<string>, HashSet<int>>? findReceivers = null)
    {
        ProfileId = profileId;
        _definition = definition;
        _control = control;
        _activateReceiver = activateReceiver;
        _findReceivers = findReceivers ?? FindReceivers;
        _logger = logger;
        _receiversChanged = receiversChanged;
        _receivers = _findReceivers(definition.ReceiverProcesses);
        _startExecutable = _receivers.Count == 0;
        _receiverSeen = _receivers.Count != 0;
        _stop = new();
    }

    public string ProfileId { get; }

    public bool StopReceiversWhenPipeCloses { get; set; } = true;

    public int[] ReceiverProcessIds
    {
        get
        {
            lock (_gate)
            {
                return [.. _receivers];
            }
        }
    }

    public void Start()
    {
        CancellationToken cancellationToken = _stop.Token;
        _ = Task.Run(() => RunAsync(cancellationToken), CancellationToken.None);
    }

    public Task StopClientAsync()
    {
        return _control.StopAsync();
    }

    public int StopReceivers()
    {
        int stopped = 0;
        foreach (int processId in ReceiverProcessIds)
        {
            try
            {
                using Process process = Process.GetProcessById(processId);
                if (process.CloseMainWindow() && process.WaitForExit(ReceiverCloseTimeout))
                {
                    stopped++;
                    continue;
                }

                process.Kill(entireProcessTree: true);
                process.WaitForExit();
                stopped++;
            }
            catch (Exception exception) when (
                exception is ArgumentException or InvalidOperationException or Win32Exception or NotSupportedException)
            {
            }
        }

        LogProfileReceiversStopped(_logger, stopped, ProfileId, null);
        return stopped;
    }

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_startExecutable && !string.IsNullOrWhiteSpace(_definition.Executable))
            {
                ProcessStartInfo start = new()
                {
                    FileName = _definition.Executable,
                    Arguments = _definition.Arguments ?? string.Empty,
                    WorkingDirectory = _definition.WorkingDirectory ?? AppContext.BaseDirectory,
                    UseShellExecute = false,
                };

                _ = Process.Start(start) ??
                    throw new InvalidOperationException($"Could not launch {_definition.Executable}.");
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                HashSet<int> observedReceivers = _findReceivers(_definition.ReceiverProcesses);
                int[] receiverProcessIds;
                bool changed;
                bool stopClient;
                lock (_gate)
                {
                    HashSet<int> previous = [.. _receivers];
                    _ = _receivers.RemoveWhere(processId => !observedReceivers.Contains(processId));
                    _receivers.UnionWith(observedReceivers);
                    _receiverSeen |= _receivers.Count != 0;
                    stopClient = _receiverSeen && _receivers.Count == 0 && !_clientStopRequested;
                    _clientStopRequested |= stopClient;
                    _activationCompleted.IntersectWith(_receivers);
                    _windowNotFoundLogged.IntersectWith(_receivers);
                    receiverProcessIds = [.. _receivers.Where(processId => !_activationCompleted.Contains(processId))];
                    changed = !previous.SetEquals(_receivers);
                }

                ActivateReceivers(receiverProcessIds);
                if (changed)
                {
                    _receiversChanged();
                }

                if (stopClient)
                {
                    StopReceiversWhenPipeCloses = false;
                    await StopClientAsync().ConfigureAwait(false);
                    LogClientStopRequestedAfterReceiversExited(_logger, ProfileId, null);
                }

                await Task.Delay(ReceiverPollInterval, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            LogProfileSessionFailed(_logger, ProfileId, exception.Message, null);
        }
    }

    private void ActivateReceivers(int[] receiverProcessIds)
    {
        foreach (int receiverProcessId in receiverProcessIds)
        {
            ReceiverWindowActivationResult result = _activateReceiver(receiverProcessId);
            if (result == ReceiverWindowActivationResult.WindowNotFound)
            {
                lock (_gate)
                {
                    if (_windowNotFoundLogged.Add(receiverProcessId))
                    {
                        LogReceiverWindowNotFound(_logger, ProfileId, receiverProcessId, null);
                    }
                }

                continue;
            }

            lock (_gate)
            {
                if (!_receivers.Contains(receiverProcessId))
                {
                    continue;
                }

                _ = _activationCompleted.Add(receiverProcessId);
            }

            if (result == ReceiverWindowActivationResult.Activated)
            {
                LogReceiverActivationSucceeded(_logger, ProfileId, receiverProcessId, null);
            }
            else if (result == ReceiverWindowActivationResult.Rejected)
            {
                LogReceiverActivationRejected(_logger, ProfileId, receiverProcessId, null);
            }
        }
    }

    private static HashSet<int> FindReceivers(IReadOnlyList<string> processNames)
    {
        HashSet<int> processIds = [];
        foreach (string processName in processNames)
        {
            string normalized = Path.GetFileNameWithoutExtension(processName.Trim());
            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            foreach (Process process in Process.GetProcessesByName(normalized))
            {
                try
                {
                    _ = processIds.Add(process.Id);
                }
                catch (InvalidOperationException)
                {
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        return processIds;
    }

    private static readonly Action<ILogger, string, string, Exception?> LogProfileSessionFailed =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(1, nameof(LogProfileSessionFailed)),
            "Profile session failed for profile {ProfileId}: {Message}");

    private static readonly Action<ILogger, int, string, Exception?> LogProfileReceiversStopped =
        LoggerMessage.Define<int, string>(
            LogLevel.Information,
            new EventId(2, nameof(LogProfileReceiversStopped)),
            "Stopped {ReceiverCount} receiver process(es) for profile {ProfileId}.");

    private static readonly Action<ILogger, string, int, Exception?> LogReceiverWindowNotFound =
        LoggerMessage.Define<string, int>(
            LogLevel.Information,
            new EventId(3, nameof(LogReceiverWindowNotFound)),
            "Receiver window not found yet for profile {ProfileId} receiver process {ProcessId}; activation will retry.");

    private static readonly Action<ILogger, string, int, Exception?> LogReceiverActivationSucceeded =
        LoggerMessage.Define<string, int>(
            LogLevel.Information,
            new EventId(4, nameof(LogReceiverActivationSucceeded)),
            "Activated receiver window for profile {ProfileId} receiver process {ProcessId}.");

    private static readonly Action<ILogger, string, int, Exception?> LogReceiverActivationRejected =
        LoggerMessage.Define<string, int>(
            LogLevel.Warning,
            new EventId(5, nameof(LogReceiverActivationRejected)),
            "Windows rejected foreground activation for profile {ProfileId} receiver process {ProcessId}.");

    private static readonly Action<ILogger, string, Exception?> LogClientStopRequestedAfterReceiversExited =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(6, nameof(LogClientStopRequestedAfterReceiversExited)),
            "Client stop requested for profile {ProfileId} because all receiver processes exited.");
}
