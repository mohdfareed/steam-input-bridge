using System;
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

        CancellationTokenSource stop = new();
        ProfileSession session = new(connectionId, profile, control, stop);
        _sessions[connectionId] = session;
        session.Task = Task.Run(() => RunSessionAsync(session, stop.Token), CancellationToken.None);
    }

    private void StopSession(Guid connectionId)
    {
        if (_sessions.Remove(connectionId, out ProfileSession? session))
        {
            session.Stop();
        }
    }

    private void StopSessions()
    {
        foreach (ProfileSession session in _sessions.Values)
        {
            session.Stop();
        }

        _sessions.Clear();
    }

    private async Task StopSessionClientAsync(Guid connectionId)
    {
        ProfileSession session;
        lock (_gate)
        {
            if (!_sessions.TryGetValue(connectionId, out ProfileSession? existing))
            {
                throw new InvalidOperationException($"Profile session '{connectionId}' is not connected.");
            }

            session = existing;
        }

        int stopped = StopReceivers(session.Profile);
        LogProfileReceiversStopped(logger, session.Profile.Id, stopped, null);
        await session.Control.StopAsync().ConfigureAwait(false);
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
            bool hasReceiver = FindReceivers(session.Profile.ReceiverProcesses).Count != 0;
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
        CancellationTokenSource stop)
    {
        public Guid ConnectionId { get; } = connectionId;

        public ResolvedProfile Profile { get; } = profile;

        public IBridgeClientApi Control { get; } = control;

        public Task? Task { get; set; }

        public void Stop()
        {
            stop.Cancel();
            stop.Dispose();
        }
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
