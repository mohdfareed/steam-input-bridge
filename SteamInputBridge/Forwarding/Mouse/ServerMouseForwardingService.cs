using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Hosting;
using SteamInputBridge.Inputs.Mouse;
using SteamInputBridge.Outputs.Mouse;
using SteamInputBridge.Outputs.Teensy;
using SteamInputBridge.Profiles;
using SteamInputBridge.Shortcuts;
using SteamInputBridge.Shortcuts.Runtime;

namespace SteamInputBridge.Forwarding.Mouse;

/// <summary>Routes raw mouse input to the active profile mouse output.</summary>
public sealed partial class ServerMouseForwardingService : IHostedService, IAsyncDisposable
{
    private readonly ActiveProfileService _profiles;
    private readonly IShortcutSource _shortcuts;
    private readonly IMouseInputSourceFactory _inputFactory;
    private readonly IMouseOutputFactory _outputFactory;
    private readonly TeensyMouseOutputService? _teensy;
    private readonly ILogger<ServerMouseForwardingService> _logger;
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly ShortcutSwitch _pointerSwitch = new();

    private IMouseInputSource? _input;
    private IMouseOutput? _output;
    private MouseOutput _outputKind = MouseOutput.None;
    private bool _pointerEnabled = true;
    private bool _clientOwnsOutput;
    private IDisposable? _teensyPause;
    private Task? _inputTask;
    private bool _disposed;

    /// <summary>Creates server-side mouse forwarding.</summary>
    public ServerMouseForwardingService(
        ActiveProfileService profiles,
        ShortcutService shortcuts,
        IMouseInputSourceFactory inputFactory,
        IMouseOutputFactory outputFactory,
        TeensyMouseOutputService teensy,
        ILogger<ServerMouseForwardingService> logger)
        : this(
            profiles,
            shortcuts ?? throw new ArgumentNullException(nameof(shortcuts)),
            inputFactory,
            outputFactory,
            logger,
            teensy)
    {
    }

    internal ServerMouseForwardingService(
        ActiveProfileService profiles,
        IShortcutSource shortcuts,
        IMouseInputSourceFactory inputFactory,
        IMouseOutputFactory outputFactory,
        ILogger<ServerMouseForwardingService> logger,
        TeensyMouseOutputService? teensy = null)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(shortcuts);
        ArgumentNullException.ThrowIfNull(inputFactory);
        ArgumentNullException.ThrowIfNull(outputFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _profiles = profiles;
        _shortcuts = shortcuts;
        _inputFactory = inputFactory;
        _outputFactory = outputFactory;
        _logger = logger;
        _teensy = teensy;
    }

    // MARK: Publics
    // ========================================================================

    /// <summary>Current mouse forwarding status.</summary>
    public BridgeMouseStatus Status
    {
        get
        {
            IMouseOutput? output;
            MouseOutput outputKind;
            bool pointerEnabled;
            lock (_gate)
            {
                output = _output;
                outputKind = _outputKind;
                pointerEnabled = _pointerEnabled;
            }

            bool outputConnected = output?.IsConnected == true;
            bool forwarding = outputConnected &&
                pointerEnabled &&
                _profiles.ActiveProfile?.Definition.MouseOutput == outputKind;
            return new(
                outputKind.ToString(),
                outputConnected,
                pointerEnabled,
                forwarding);
        }
    }

    /// <summary>Raised when the mouse pointer shortcut gate changes.</summary>
    public event EventHandler? PointerEnabledChanged;

    /// <summary>Transfers mouse output ownership between the server and active client.</summary>
    public async Task SetClientOwnsOutputAsync(
        bool clientOwnsOutput,
        bool clientUsesTeensy,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _clientOwnsOutput = clientOwnsOutput;
        }

        if (clientOwnsOutput)
        {
            await RefreshOutputAsync(cancellationToken).ConfigureAwait(false);
            if (clientUsesTeensy && _teensyPause is null)
            {
                _teensyPause = _teensy?.PauseConnection();
            }
            else if (!clientUsesTeensy)
            {
                ReleaseTeensyPause();
            }
        }
        else
        {
            ReleaseTeensyPause();
            await RefreshOutputAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    // MARK: Lifecycle
    // ========================================================================

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _profiles.ProfilesChanged += OnProfilesChanged;
        _shortcuts.Shortcut += OnShortcut;

        await RefreshOutputAsync(cancellationToken).ConfigureAwait(false);
        _input = await _inputFactory.ConnectAsync(cancellationToken).ConfigureAwait(false);
        _inputTask = Task.Run(() => RunInput(_stop.Token), CancellationToken.None);
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _profiles.ProfilesChanged -= OnProfilesChanged;
        _shortcuts.Shortcut -= OnShortcut;

        await _stop.CancelAsync().ConfigureAwait(false);
        if (_inputTask is not null)
        {
            try
            {
                await _inputTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
            }
        }

        await DisposeOutputAsync().ConfigureAwait(false);
        ReleaseTeensyPause();
        if (_input is not null)
        {
            await _input.DisposeAsync().ConfigureAwait(false);
            _input = null;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _profiles.ProfilesChanged -= OnProfilesChanged;
        _shortcuts.Shortcut -= OnShortcut;

        await _stop.CancelAsync().ConfigureAwait(false);
        _stop.Dispose();

        await DisposeOutputAsync().ConfigureAwait(false);
        ReleaseTeensyPause();
        if (_input is not null)
        {
            await _input.DisposeAsync().ConfigureAwait(false);
            _input = null;
        }
    }

    // MARK: Events
    // ========================================================================

    private void OnProfilesChanged(object? sender, ProfilesChangedEventArgs args)
    {
        _ = sender;
        _ = args;
        _ = RefreshOutputAsync(CancellationToken.None);
    }

    private void ReleaseTeensyPause()
    {
        _teensyPause?.Dispose();
        _teensyPause = null;
    }

    private void OnShortcut(object? sender, ShortcutEventArgs args)
    {
        _ = sender;
        if (args.Target.Target != ShortcutTarget.MousePointer)
        {
            return;
        }

        bool enabled = _pointerSwitch.Apply(args.ShortcutId, args.Action, args.Phase, defaultEnabled: true);
        IMouseOutput? clear = null;
        lock (_gate)
        {
            if (_pointerEnabled == enabled)
            {
                return;
            }

            _pointerEnabled = enabled;
            if (!enabled)
            {
                clear = _output;
            }
        }

        Clear(clear);
        PointerEnabledChanged?.Invoke(this, EventArgs.Empty);
    }

    // MARK: Logging
    // ========================================================================

    private static readonly Action<ILogger, string, Exception?> LogMouseInputFailed =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(1, nameof(LogMouseInputFailed)),
            "Mouse input failed: {Message}");

    private static readonly Action<ILogger, MouseOutput, string, Exception?> LogMouseOutputFailed =
        LoggerMessage.Define<MouseOutput, string>(
            LogLevel.Warning,
            new EventId(2, nameof(LogMouseOutputFailed)),
            "Mouse output {Output} failed: {Message}");
}
