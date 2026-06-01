using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Inputs.Mouse;
using SteamInputBridge.Outputs.Mouse;
using SteamInputBridge.Settings;

namespace SteamInputBridge.Outputs.Teensy;

/// <summary>Maintains the Teensy board connection used by mouse output.</summary>
public sealed class TeensyMouseOutputService : BackgroundService
{
    private static readonly TimeSpan DefaultSearchInterval = TimeSpan.FromSeconds(1);
    private static readonly Action<ILogger, TeensyConnectionState, string, string, Exception?> LogTeensyStatus =
        LoggerMessage.Define<TeensyConnectionState, string, string>(
            LogLevel.Information,
            new EventId(1, nameof(LogTeensyStatus)),
            "Teensy board {State}. ConfiguredPort={ConfiguredPort}, ConnectedPort={ConnectedPort}.");

    private readonly SettingsService _settings;
    private readonly TeensyPortDiscovery _ports;
    private readonly TeensySerialConnection _connection;
    private readonly ILogger<TeensyMouseOutputService> _logger;
    private readonly TimeSpan _searchInterval;
    private readonly Lock _gate = new();
    private readonly byte[] _frame = new byte[TeensyProtocol.FrameSize];

    private string? _configuredPort;
    private string? _connectedPort;
    private TeensyConnectionState _state = TeensyConnectionState.Connecting;
    private byte _sequence;

    /// <summary>Creates the Teensy mouse output service.</summary>
    public TeensyMouseOutputService(SettingsService settings, ILogger<TeensyMouseOutputService> logger)
        : this(
            settings,
            new TeensyPortDiscovery(),
            new TeensySerialConnection(),
            logger,
            DefaultSearchInterval)
    {
    }

    internal TeensyMouseOutputService(
        SettingsService settings,
        TeensyPortDiscovery ports,
        TeensySerialConnection connection,
        ILogger<TeensyMouseOutputService> logger,
        TimeSpan searchInterval)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(ports);
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(logger);

        _settings = settings;
        _ports = ports;
        _connection = connection;
        _logger = logger;
        _searchInterval = searchInterval;
        _configuredPort = TeensyPortDiscovery.NormalizeConfiguredPort(settings.Current.Teensy.Port);
    }

    /// <summary>Raised after the board status changes.</summary>
    public event EventHandler? StatusChanged;

    /// <summary>Current Teensy connection status.</summary>
    public TeensyOutputStatus Status
    {
        get
        {
            lock (_gate)
            {
                return new(_state, _configuredPort, _connectedPort);
            }
        }
    }

    /// <summary>Whether the board serial connection is open.</summary>
    public bool IsConnected
    {
        get
        {
            lock (_gate)
            {
                return _connection.IsConnected;
            }
        }
    }

    /// <summary>Creates an IMouseOutput adapter for the shared board connection.</summary>
    public IMouseOutput CreateOutput()
    {
        return new TeensyMouseOutput(this);
    }

    internal ValueTask SendAsync(in MouseInput input, CancellationToken cancellationToken = default)
    {
        return SendReportAsync(input.Report, cancellationToken);
    }

    internal ValueTask SendReportAsync(MouseReport report, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool changed = false;
        lock (_gate)
        {
            if (!_connection.IsConnected)
            {
                return ValueTask.CompletedTask;
            }

            int bytes = TeensyProtocol.WriteMouseReport(_frame, _sequence++, report);
            if (!_connection.TryWrite(_frame, bytes))
            {
                changed = SetConnectingLocked();
            }
        }

        RaiseStatusChanged(changed);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _settings.Changed += OnSettingsChanged;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                bool changed = RefreshConnection();
                RaiseStatusChanged(changed);
                await Task.Delay(_searchInterval, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            _settings.Changed -= OnSettingsChanged;
            lock (_gate)
            {
                _connection.Close();
                _connectedPort = null;
                _state = TeensyConnectionState.Connecting;
            }
        }
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        lock (_gate)
        {
            _connection.Dispose();
        }

        base.Dispose();
    }

    private void OnSettingsChanged(object? sender, ApplicationSettingsChangedEventArgs args)
    {
        _ = sender;
        bool changed;
        lock (_gate)
        {
            string? newPort = TeensyPortDiscovery.NormalizeConfiguredPort(args.Settings.Teensy.Port);
            if (string.Equals(_configuredPort, newPort, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _configuredPort = newPort;
            changed = SetConnectingLocked();
        }

        RaiseStatusChanged(changed);
    }

    private bool RefreshConnection()
    {
        lock (_gate)
        {
            if (_connection.IsConnected)
            {
                string? connectedPort = _connection.PortName;
                return !string.IsNullOrWhiteSpace(connectedPort) && _ports.PortExists(connectedPort)
                    ? SetConnectedLocked(connectedPort)
                    : SetConnectingLocked();
            }

            return _connection.TryConnect(_ports.GetCandidatePorts(_configuredPort))
                ? SetConnectedLocked(_connection.PortName)
                : SetConnectingLocked();
        }
    }

    private bool SetConnectedLocked(string? connectedPort)
    {
        connectedPort = string.IsNullOrWhiteSpace(connectedPort) ? null : connectedPort;
        bool changed = _state != TeensyConnectionState.Connected ||
            !string.Equals(_connectedPort, connectedPort, StringComparison.OrdinalIgnoreCase);
        _state = TeensyConnectionState.Connected;
        _connectedPort = connectedPort;
        return changed;
    }

    private bool SetConnectingLocked()
    {
        bool changed = _state != TeensyConnectionState.Connecting || _connectedPort is not null;
        _connection.Close();
        _state = TeensyConnectionState.Connecting;
        _connectedPort = null;
        return changed;
    }

    private void RaiseStatusChanged(bool changed)
    {
        if (changed)
        {
            LogStatus();
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void LogStatus()
    {
        TeensyOutputStatus status = Status;
        LogTeensyStatus(
            _logger,
            status.State,
            status.ConfiguredPort ?? "Auto",
            status.ConnectedPort ?? "None",
            null);
    }
}

/// <summary>Teensy board connection state.</summary>
public enum TeensyConnectionState
{
    /// <summary>The service is searching for a board.</summary>
    Connecting,

    /// <summary>The service has an open serial connection to a board.</summary>
    Connected,
}

/// <summary>Current Teensy output status.</summary>
public readonly record struct TeensyOutputStatus(
    TeensyConnectionState State,
    string? ConfiguredPort,
    string? ConnectedPort);
