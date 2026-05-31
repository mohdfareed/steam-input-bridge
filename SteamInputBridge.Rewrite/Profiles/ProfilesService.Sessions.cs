using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Hosting;

namespace SteamInputBridge.Profiles;

public sealed partial class ProfilesService
{
    private static readonly TimeSpan ReceiverPollInterval = TimeSpan.FromMilliseconds(250);

    // MARK: Sessions
    // ========================================================================

    private void StartSession(Guid connectionId, ResolvedProfile profile, IBridgeClientApi control)
    {
        StopSession(connectionId);
        HashSet<int> receiverBaseline = string.IsNullOrWhiteSpace(profile.Executable)
            ? []
            : FindReceivers(profile.ReceiverProcesses);

#pragma warning disable CA2000 // CancellationTokenSource ownership transfers to ProfileSession.
        CancellationTokenSource stop = new();
        ProfileSession session = new(connectionId, profile, control, receiverBaseline, stop);
#pragma warning restore CA2000

        _sessions[connectionId] = session;
        session.Task = Task.Run(() => RunSessionAsync(session, stop.Token), CancellationToken.None);
    }

    private void StopSession(Guid connectionId)
    {
        if (_sessions.Remove(connectionId, out ProfileSession? session))
        {
            session.Cancel();
        }
    }

    private void StopSessions()
    {
        foreach (ProfileSession session in _sessions.Values)
        {
            session.Cancel();
        }

        _sessions.Clear();
    }

    private async Task RunSessionAsync(ProfileSession session, CancellationToken cancellationToken)
    {
        try
        {
            LaunchProfile(session.Profile);
            await WatchReceiversAsync(session, cancellationToken).ConfigureAwait(false);
            await session.Control.StopAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            LogProfileSessionFailed(logger, session.Profile.Id, exception.Message, null);
        }
    }

    private static void LaunchProfile(ResolvedProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Executable))
        {
            return;
        }

        ProcessStartInfo start = new()
        {
            FileName = profile.Executable,
            Arguments = profile.Arguments ?? string.Empty,
            WorkingDirectory = profile.WorkingDirectory ?? AppContext.BaseDirectory,
            UseShellExecute = false,
        };

        _ = Process.Start(start) ?? throw new InvalidOperationException($"Could not launch {profile.Executable}.");
    }

    private static async Task WatchReceiversAsync(ProfileSession session, CancellationToken cancellationToken)
    {
        if (session.Profile.ReceiverProcesses.Count == 0)
        {
            return;
        }

        bool sawReceiver = false;
        while (!cancellationToken.IsCancellationRequested)
        {
            bool hasReceiver = RefreshSessionReceivers(session);
            sawReceiver |= hasReceiver;
            if (sawReceiver && !hasReceiver)
            {
                return;
            }

            await Task.Delay(ReceiverPollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class ProfileSession(
        Guid connectionId,
        ResolvedProfile profile,
        IBridgeClientApi control,
        HashSet<int> receiverBaseline,
        CancellationTokenSource stop)
    {
        public Guid ConnectionId { get; } = connectionId;

        public ResolvedProfile Profile { get; } = profile;

        public IBridgeClientApi Control { get; } = control;

        public HashSet<int> ReceiverBaseline { get; } = receiverBaseline;

        public HashSet<int> Receivers { get; } = [];

        public Task? Task { get; set; }

        public ReceiverDisconnectAction ReceiversOnDisconnect { get; set; } = ReceiverDisconnectAction.Stop;

        public void Cancel()
        {
            stop.Cancel();
            stop.Dispose();
        }
    }

    private enum ReceiverDisconnectAction
    {
        Stop,
        Keep,
    }

    // MARK: Logging
    // ========================================================================

    private static readonly Action<ILogger, string, string, Exception?> LogProfileSessionFailed =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(2, nameof(LogProfileSessionFailed)),
            "Profile session failed for profile {ProfileId}: {Message}");

    private static readonly Action<ILogger, string, int, Exception?> LogProfileReceiversStopped =
        LoggerMessage.Define<string, int>(
            LogLevel.Information,
            new EventId(3, nameof(LogProfileReceiversStopped)),
            "Stopped {ReceiverCount} receiver process(es) for profile {ProfileId}.");
}
