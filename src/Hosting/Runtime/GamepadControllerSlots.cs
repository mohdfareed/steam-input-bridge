using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inputs;
using Inputs.Sdl;
using Microsoft.Extensions.Logging;
using Outputs;
using Outputs.Viiper;

namespace Hosting;

internal sealed class GamepadControllerRegistry(
    ViiperOptions viiperOptions,
    ForwardingHostState hostState,
    ILogger? logger) : IAsyncDisposable
{
    private readonly ViiperOptions _viiperOptions = viiperOptions;
    private readonly ForwardingHostState _hostState = hostState;
    private readonly ILogger? _logger = logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<SdlControllerId, GamepadControllerSlot> _slots = [];
    private readonly Dictionary<Guid, GamepadClientSession> _sessions = [];
    private bool _reclaimedOutputs;

    public async Task<GamepadReportSessionInfo> AttachSteamControllerAsync(
        SdlControllerInfo steamController,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(steamController);
        if (steamController.Source != SdlControllerSource.Steam)
        {
            throw new InvalidOperationException("Only Steam Input controllers can attach to the host gamepad route.");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await PrepareOutputsAsync(cancellationToken).ConfigureAwait(false);

            SdlControllerInfo physicalController = ResolvePhysicalController(steamController);
            if (!_slots.TryGetValue(physicalController.Id, out GamepadControllerSlot? slot))
            {
                slot = await GamepadControllerSlot.CreateAsync(
                        physicalController,
                        _viiperOptions,
                        _hostState,
                        _logger,
                        cancellationToken)
                    .ConfigureAwait(false);
                _slots.Add(physicalController.Id, slot);
            }

            Guid sessionId = Guid.NewGuid();
            GamepadClientSession session = new(sessionId, slot, DetachPipeSession);
            slot.AttachClient(sessionId, steamController.Id);
            _sessions.Add(sessionId, session);
            session.Start();
            GamepadControllerSlotLog.ClientAttached(_logger, physicalController.Name, sessionId);
            return session.Info;
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    public async Task DetachAsync(Guid sessionId)
    {
        GamepadClientSession? session = await RemoveSessionAsync(sessionId).ConfigureAwait(false);
        if (session is not null)
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<GamepadControllerSlotStatus>> GetStatusAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            List<GamepadControllerSlotStatus> statuses = new(_slots.Count);
            foreach (GamepadControllerSlot slot in _slots.Values)
            {
                statuses.Add(slot.GetStatus());
            }

            return statuses;
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        GamepadClientSession[] sessions;
        GamepadControllerSlot[] slots;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            sessions = [.. _sessions.Values];
            _sessions.Clear();
            slots = [.. _slots.Values];
            _slots.Clear();
        }
        finally
        {
            _ = _gate.Release();
        }

        foreach (GamepadClientSession session in sessions)
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }

        foreach (GamepadControllerSlot slot in slots)
        {
            await slot.DisposeAsync().ConfigureAwait(false);
        }

        _gate.Dispose();
    }

    private async Task PrepareOutputsAsync(CancellationToken cancellationToken)
    {
        if (_reclaimedOutputs)
        {
            return;
        }

        await ViiperServer.EnsureRunningAsync(_viiperOptions, cancellationToken).ConfigureAwait(false);
        await ViiperXbox360Output.ReclaimOwnedDevicesAsync(_viiperOptions, cancellationToken)
            .ConfigureAwait(false);
        _reclaimedOutputs = true;
    }

    private static SdlControllerInfo ResolvePhysicalController(SdlControllerInfo clientController)
    {
        List<SdlControllerInfo> physicalControllers = ExcludeOwnedViiperControllers(
            SdlControllerCatalog.GetControllers());
        SdlControllerInfo? match = SdlControllerMatcher.FindPhysicalController(clientController, physicalControllers);
        return match ??
            throw new InvalidOperationException(
                $"No physical controller matched client controller \"{clientController.Name}\".");
    }

    private async Task<GamepadClientSession?> RemoveSessionAsync(Guid sessionId)
    {
        GamepadClientSession? session;
        GamepadControllerSlot? slotToDispose = null;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_sessions.TryGetValue(sessionId, out session))
            {
                return null;
            }

            _ = _sessions.Remove(sessionId);
            int remainingClients = session.Slot.DetachClient(sessionId);
            GamepadControllerSlotLog.ClientDetached(
                _logger,
                session.Slot.PhysicalControllerName,
                sessionId,
                remainingClients);
            if (remainingClients == 0)
            {
                _ = _slots.Remove(session.Slot.PhysicalControllerId);
                slotToDispose = session.Slot;
            }
        }
        finally
        {
            _ = _gate.Release();
        }

        if (slotToDispose is not null)
        {
            await slotToDispose.DisposeAsync().ConfigureAwait(false);
        }

        return session;
    }

    private void DetachPipeSession(Guid sessionId)
    {
        _ = RemoveClosedPipeSessionAsync(sessionId);
    }

    private async Task RemoveClosedPipeSessionAsync(Guid sessionId)
    {
        try
        {
            _ = await RemoveSessionAsync(sessionId).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            GamepadControllerSlotLog.DetachFailed(_logger, exception);
        }
    }

    private static List<SdlControllerInfo> ExcludeOwnedViiperControllers(
        IReadOnlyList<SdlControllerInfo> controllers)
    {
        List<SdlControllerInfo> filtered = new(controllers.Count);
        foreach (SdlControllerInfo controller in controllers)
        {
            if (!ViiperXbox360Output.IsOwnedSdlDevice(controller.Name, controller.Path))
            {
                filtered.Add(controller);
            }
        }

        return filtered;
    }

    private sealed class GamepadClientSession(
        Guid id,
        GamepadControllerSlot slot,
        Action<Guid> handleClosed) : IAsyncDisposable
    {
        private readonly GamepadReportPipeServer _pipe = new(
            id,
            state => slot.SendSteamInput(id, state, CancellationToken.None),
            handleClosed);

        public Guid Id { get; } = id;

        public GamepadControllerSlot Slot { get; } = slot;

        public GamepadReportSessionInfo Info => new(Id, _pipe.PipeName);

        public void Start()
        {
            _pipe.Start();
        }

        public ValueTask DisposeAsync()
        {
            return _pipe.DisposeAsync();
        }
    }
}

internal sealed class GamepadControllerSlot : IAsyncDisposable
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(1);

    private readonly SdlControllerInfo _physicalController;
    private readonly ForwardingHostState _hostState;
    private readonly ViiperXbox360Output _output;
    private readonly ILogger? _logger;
    private readonly Lock _stateGate = new();
    private readonly Lock _rumbleGate = new();
    private readonly HashSet<Guid> _clients = [];
    private readonly Dictionary<Guid, SdlControllerId> _clientControllers = [];
    private readonly IDisposable _rumbleSubscription;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _physicalTask;
    private SdlGamepadSource? _physicalRumbleSink;
    private GamepadState _latestSteam;
    private GamepadState _latestPhysical;
    private Xbox360Report _latestReport;
    private GamepadRumble _latestRumble;
    private int _physicalConnected;
    private int _disposed;
    private bool _hasSteam;
    private bool _hasReport;

    private GamepadControllerSlot(
        SdlControllerInfo physicalController,
        ForwardingHostState hostState,
        ViiperXbox360Output output,
        ILogger? logger)
    {
        _physicalController = physicalController;
        _hostState = hostState;
        _output = output;
        _logger = logger;
        _rumbleSubscription = output.ListenRumble(rumble =>
        {
            SetRumble(GamepadForwardingExtensions.ToGamepadRumble(rumble));
            return ValueTask.CompletedTask;
        });
        _physicalTask = Task.Run(RunPhysicalInput, CancellationToken.None);
    }

    public SdlControllerId PhysicalControllerId => _physicalController.Id;

    public string PhysicalControllerName => _physicalController.Name;

    public static async Task<GamepadControllerSlot> CreateAsync(
        SdlControllerInfo physicalController,
        ViiperOptions viiperOptions,
        ForwardingHostState hostState,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(physicalController);
        await ViiperServer.EnsureRunningAsync(viiperOptions, cancellationToken).ConfigureAwait(false);

        ViiperXbox360Output? output = null;
        try
        {
            output = await ViiperXbox360Output.ConnectAsync(viiperOptions, cancellationToken).ConfigureAwait(false);
            GamepadControllerSlot slot = new(physicalController, hostState, output, logger);
            output = null;
            GamepadControllerSlotLog.Created(logger, physicalController.Name);
            return slot;
        }
        finally
        {
            if (output is not null)
            {
                await output.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public void AttachClient(Guid sessionId, SdlControllerId steamControllerId)
    {
        lock (_stateGate)
        {
            if (!CanAttachController(steamControllerId))
            {
                throw new InvalidOperationException(
                    $"Physical controller \"{_physicalController.Name}\" is already attached to a different Steam Input controller.");
            }

            _ = _clients.Add(sessionId);
            _clientControllers.Add(sessionId, steamControllerId);
        }
    }

    public int DetachClient(Guid sessionId)
    {
        lock (_stateGate)
        {
            _ = _clients.Remove(sessionId);
            _ = _clientControllers.Remove(sessionId);
            return _clients.Count;
        }
    }

    public GamepadControllerSlotStatus GetStatus()
    {
        lock (_stateGate)
        {
            return new GamepadControllerSlotStatus(
                _physicalController.Id,
                _physicalController.Name,
                _clients.Count,
                Volatile.Read(ref _physicalConnected) != 0,
                _output.IsConnected,
                _output.BusId,
                _output.DeviceId);
        }
    }

    public void SendSteamInput(Guid sessionId, GamepadState state, CancellationToken cancellationToken)
    {
        lock (_stateGate)
        {
            if (!_clients.Contains(sessionId))
            {
                return;
            }

            _latestSteam = state;
            _hasSteam = true;
            SendCombined(cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        GamepadControllerSlotLog.Disposing(_logger, _physicalController.Name);
        await _stop.CancelAsync().ConfigureAwait(false);
        _rumbleSubscription.Dispose();
        ClearRumbleSink();
        await _output.DisposeAsync().ConfigureAwait(false);
        GamepadControllerSlotLog.Disposed(_logger, _physicalController.Name);
        _ = ObservePhysicalTaskAsync(_physicalTask, _stop, _logger, _physicalController.Name);
    }

    private async Task RunPhysicalInput()
    {
        while (!_stop.IsCancellationRequested)
        {
            SdlGamepadSource? source = null;
            try
            {
                source = await SdlGamepadSource.ConnectAsync(_physicalController.Id, _stop.Token)
                    .ConfigureAwait(false);
                SetPhysicalConnected(source);
                source.Run(HandlePhysicalInput, _stop.Token);
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
            }
            catch (SdlGamepadDisconnectedException)
            {
            }
            catch (InvalidOperationException)
            {
            }
            finally
            {
                SetPhysicalDisconnected();
                if (source is not null)
                {
                    await source.DisposeAsync().ConfigureAwait(false);
                }
            }

            await DelayReconnectAsync(_stop.Token).ConfigureAwait(false);
        }
    }

    private void HandlePhysicalInput(in GamepadInput input)
    {
        lock (_stateGate)
        {
            _latestPhysical = input.State;
            SendCombined(_stop.Token);
        }
    }

    private void SetPhysicalConnected(SdlGamepadSource source)
    {
        lock (_rumbleGate)
        {
            _physicalRumbleSink = source;
            if (_latestRumble != GamepadRumble.Empty)
            {
                _ = source.TryRumble(_latestRumble);
            }
        }

        if (Interlocked.Exchange(ref _physicalConnected, 1) == 0)
        {
            GamepadControllerSlotLog.PhysicalConnected(_logger, _physicalController.Name);
        }
    }

    private void SetPhysicalDisconnected()
    {
        ClearRumbleSink();
        if (Interlocked.Exchange(ref _physicalConnected, 0) != 0)
        {
            lock (_stateGate)
            {
                _latestPhysical = default;
            }

            GamepadControllerSlotLog.PhysicalDisconnected(_logger, _physicalController.Name);
        }
    }

    private void SetRumble(GamepadRumble rumble)
    {
        lock (_rumbleGate)
        {
            _latestRumble = rumble;
            _ = _physicalRumbleSink?.TryRumble(rumble);
        }
    }

    private void ClearRumbleSink()
    {
        lock (_rumbleGate)
        {
            _ = _physicalRumbleSink?.TryRumble(GamepadRumble.Empty);
            _physicalRumbleSink = null;
        }
    }

    private void SendCombined(CancellationToken cancellationToken)
    {
        if (!_hasSteam || !_hostState.EmulationEnabled)
        {
            return;
        }

        GamepadState combined = _hostState.PhysicalMotionEnabled && _latestPhysical.Motion != default
            ? _latestSteam with { Motion = _latestPhysical.Motion }
            : _latestSteam;
        Xbox360Report report = GamepadForwardingExtensions.ToXbox360Report(combined);
        if (_hasReport && report == _latestReport)
        {
            return;
        }

        GamepadForwardingExtensions.SendSynchronously(_output, report, cancellationToken);
        _latestReport = report;
        _hasReport = true;
    }

    private bool CanAttachController(SdlControllerId steamControllerId)
    {
        foreach (SdlControllerId existingControllerId in _clientControllers.Values)
        {
            if (!string.Equals(
                existingControllerId.Value,
                steamControllerId.Value,
                StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static async Task DelayReconnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(ReconnectDelay, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static async Task ObservePhysicalTaskAsync(
        Task physicalTask,
        CancellationTokenSource stop,
        ILogger? logger,
        string physicalControllerName)
    {
        try
        {
            await physicalTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
        }
#pragma warning disable CA1031 // Background observer logs and swallows reader failures after slot disposal.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            GamepadControllerSlotLog.PhysicalReaderFailed(logger, physicalControllerName, exception);
        }
        finally
        {
            stop.Dispose();
        }
    }
}

/// <summary>Server-owned gamepad slot status.</summary>
/// <param name="PhysicalControllerId">Physical SDL controller id.</param>
/// <param name="PhysicalControllerName">Physical SDL controller name.</param>
/// <param name="AttachedClients">Attached Steam Input clients.</param>
/// <param name="InputConnected">Whether physical input is connected.</param>
/// <param name="OutputConnected">Whether virtual output is connected.</param>
/// <param name="OutputBusId">VIIPER output bus id.</param>
/// <param name="OutputDeviceId">VIIPER output device id.</param>
public readonly record struct GamepadControllerSlotStatus(
    SdlControllerId PhysicalControllerId,
    string PhysicalControllerName,
    int AttachedClients,
    bool InputConnected,
    bool OutputConnected,
    uint? OutputBusId,
    string? OutputDeviceId);

internal static class GamepadControllerSlotLog
{
    private static readonly Action<ILogger, string, Exception?> CreatedMessage =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(1, nameof(Created)),
            "Created gamepad controller slot for {PhysicalControllerName}.");

    private static readonly Action<ILogger, string, Exception?> PhysicalConnectedMessage =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(2, nameof(PhysicalConnected)),
            "Physical gamepad connected for {PhysicalControllerName}.");

    private static readonly Action<ILogger, string, Exception?> PhysicalDisconnectedMessage =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(3, nameof(PhysicalDisconnected)),
            "Physical gamepad disconnected for {PhysicalControllerName}.");

    private static readonly Action<ILogger, Exception?> DetachFailedMessage =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(4, nameof(DetachFailed)),
            "Gamepad client detach failed.");

    private static readonly Action<ILogger, string, Guid, Exception?> ClientAttachedMessage =
        LoggerMessage.Define<string, Guid>(
            LogLevel.Information,
            new EventId(5, nameof(ClientAttached)),
            "Gamepad client {SessionId} attached to {PhysicalControllerName}.");

    private static readonly Action<ILogger, string, Guid, int, Exception?> ClientDetachedMessage =
        LoggerMessage.Define<string, Guid, int>(
            LogLevel.Information,
            new EventId(6, nameof(ClientDetached)),
            "Gamepad client {SessionId} detached from {PhysicalControllerName}. Remaining clients: {RemainingClients}.");

    private static readonly Action<ILogger, string, Exception?> DisposingMessage =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(7, nameof(Disposing)),
            "Disposing gamepad controller slot for {PhysicalControllerName}.");

    private static readonly Action<ILogger, string, Exception?> DisposedMessage =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(8, nameof(Disposed)),
            "Disposed gamepad controller slot for {PhysicalControllerName}.");

    private static readonly Action<ILogger, string, Exception?> PhysicalReaderFailedMessage =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(9, nameof(PhysicalReaderFailed)),
            "Physical gamepad reader failed for {PhysicalControllerName}.");

    public static void Created(ILogger? logger, string physicalControllerName)
    {
        if (logger is not null)
        {
            CreatedMessage(logger, physicalControllerName, null);
        }
    }

    public static void PhysicalConnected(ILogger? logger, string physicalControllerName)
    {
        if (logger is not null)
        {
            PhysicalConnectedMessage(logger, physicalControllerName, null);
        }
    }

    public static void PhysicalDisconnected(ILogger? logger, string physicalControllerName)
    {
        if (logger is not null)
        {
            PhysicalDisconnectedMessage(logger, physicalControllerName, null);
        }
    }

    public static void DetachFailed(ILogger? logger, Exception exception)
    {
        if (logger is not null)
        {
            DetachFailedMessage(logger, exception);
        }
    }

    public static void ClientAttached(ILogger? logger, string physicalControllerName, Guid sessionId)
    {
        if (logger is not null)
        {
            ClientAttachedMessage(logger, physicalControllerName, sessionId, null);
        }
    }

    public static void ClientDetached(
        ILogger? logger,
        string physicalControllerName,
        Guid sessionId,
        int remainingClients)
    {
        if (logger is not null)
        {
            ClientDetachedMessage(logger, physicalControllerName, sessionId, remainingClients, null);
        }
    }

    public static void Disposing(ILogger? logger, string physicalControllerName)
    {
        if (logger is not null)
        {
            DisposingMessage(logger, physicalControllerName, null);
        }
    }

    public static void Disposed(ILogger? logger, string physicalControllerName)
    {
        if (logger is not null)
        {
            DisposedMessage(logger, physicalControllerName, null);
        }
    }

    public static void PhysicalReaderFailed(
        ILogger? logger,
        string physicalControllerName,
        Exception exception)
    {
        if (logger is not null)
        {
            PhysicalReaderFailedMessage(logger, physicalControllerName, exception);
        }
    }
}
