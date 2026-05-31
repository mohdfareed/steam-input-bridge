using System;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace SteamInputBridge.Microphone;

/// <summary>Microphone indicator status.</summary>
public sealed record MicrophoneStatus(bool Available, bool Muted, bool IsActive)
{
    /// <summary>No usable microphone state.</summary>
    public static MicrophoneStatus Unavailable { get; } = new(Available: false, Muted: false, IsActive: false);
}

/// <summary>CoreAudio-backed microphone control.</summary>
[SupportedOSPlatform("windows")]
public sealed class MicrophoneService : IDisposable
{
    private static readonly TimeSpan StatusPollInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(1);

    private readonly CancellationTokenSource _stop = new();
    private readonly Lock _gate = new();
    private readonly Task _monitor;

    private MicrophoneConnection? _connection;
    private bool _disposed;

    /// <inheritdoc/>
    public MicrophoneService()
    {
        _monitor = Task.Run(() => MonitorAsync(_stop.Token), CancellationToken.None);
    }

    /// <summary>Raised when the observed microphone status changes.</summary>
    public event EventHandler<MicrophoneStatusChangedEventArgs>? StatusChanged;

    /// <summary>Reads the current microphone status.</summary>
    public MicrophoneStatus GetStatus()
    {
        MicrophoneConnection? connection;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            connection = _connection;
        }

        return connection is not null && connection.TryReadStatus(out MicrophoneStatus status)
            ? status
            : MicrophoneStatus.Unavailable;
    }

    /// <summary>Sets whether the current microphone should be enabled.</summary>
    public void SetEnabled(bool enabled)
    {
        MicrophoneConnection? connection;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            connection = _connection;
        }

        connection?.SetEnabled(enabled);
    }

    /// <summary>Stops the background microphone monitor.</summary>
    public void Dispose()
    {
        MicrophoneConnection? connection;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            connection = _connection;
            _disposed = true;
            _connection = null;
        }

        _stop.Cancel();
        connection?.Dispose();

        try
        {
            _monitor.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }

        _stop.Dispose();
    }

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            MicrophoneConnection? connection = null;
            try
            {
                connection = MicrophoneConnection.Open();
                connection.StatusChanged += OnStatusChanged;
                lock (_gate)
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    _connection?.Dispose();
                    _connection = connection;
                }

                while (ReferenceEquals(_connection, connection) && !cancellationToken.IsCancellationRequested)
                {
                    if (!connection.TryReadStatus(out _))
                    {
                        break;
                    }

                    await Task.Delay(StatusPollInterval, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (MicrophoneConnection.IsConnectionFailure(exception))
            {
                OnStatusChanged(MicrophoneStatus.Unavailable);
            }
            finally
            {
                lock (_gate)
                {
                    if (ReferenceEquals(_connection, connection))
                    {
                        _connection = null;
                    }
                }

                if (connection is not null)
                {
                    connection.StatusChanged -= OnStatusChanged;
                    connection.Dispose();
                }
            }

            await Task.Delay(ReconnectDelay, cancellationToken).ConfigureAwait(false);
        }
    }

    private void OnStatusChanged(MicrophoneStatus status)
    {
        StatusChanged?.Invoke(this, new(status));
    }
}

/// <summary>Microphone status change event data.</summary>
public sealed class MicrophoneStatusChangedEventArgs(MicrophoneStatus status) : EventArgs
{
    /// <summary>Current microphone status.</summary>
    public MicrophoneStatus Status { get; } = status;
}
