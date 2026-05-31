using System;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using SteamInputBridge.Microphone;

namespace SteamInputBridge.App.Tray.Overlay;

internal sealed class StatusOverlayController : IDisposable
{
    private readonly IServiceProvider _services;
    private readonly StatusOverlayWindow _window = new();
    private readonly Dispatcher _dispatcher;
    private bool _disposed;

    // MARK: Lifecycle
    // ========================================================================

    public StatusOverlayController(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        _services = services;
        _dispatcher = _window.Dispatcher;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        MicrophoneService microphone = _services.GetRequiredService<MicrophoneService>();
        microphone.StatusChanged += OnMicrophoneStatusChanged;

        _window.SetMicrophoneStatus(microphone.GetStatus());
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _services.GetRequiredService<MicrophoneService>().StatusChanged -= OnMicrophoneStatusChanged;
        _window.Close();
    }

    // MARK: Event Handlers
    // ========================================================================

    private void OnMicrophoneStatusChanged(object? sender, MicrophoneStatusChangedEventArgs args)
    {
        _ = sender;
        if (!_disposed)
        {
            _ = _dispatcher.BeginInvoke(new Action(() => _window.SetMicrophoneStatus(args.Status)));
        }
    }
}
