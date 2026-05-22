using System;
using System.ComponentModel;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Forwarding.Mouse;
using SteamInputBridge.Inputs.RawInput;

namespace SteamInputBridge.Hosting.Server.Inputs;

internal sealed class MouseInputPump(
    MouseBroker broker,
    ILogger logger) : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private RawInputMouseSource? _source;
    private Task? _task;
    private string? _lastError;
    private bool _running;
    private bool _disposed;

    public void Start(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            _lastError = "Windows is required.";
            HostingLog.RawInputMousePumpDisabled(logger);
            return;
        }

#pragma warning disable CA1416 // Guarded by the Windows check above.
        _task = Task.Run(() => RunLinkedWindows(cancellationToken), CancellationToken.None);
#pragma warning restore CA1416
    }

    public MouseInputPumpStatus GetStatus()
    {
#pragma warning disable CA1416 // Guarded by the Windows check.
        return new MouseInputPumpStatus(
            _running,
            OperatingSystem.IsWindows() && _source?.IsConnected == true,
            _lastError);
#pragma warning restore CA1416
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _stop.CancelAsync().ConfigureAwait(false);
        await IgnoreExpectedStopAsync(_task).ConfigureAwait(false);
        if (_source is not null && OperatingSystem.IsWindows())
        {
#pragma warning disable CA1416 // Guarded by the Windows check.
            await _source.DisposeAsync().ConfigureAwait(false);
#pragma warning restore CA1416
        }

        _stop.Dispose();
    }

    [SupportedOSPlatform("windows")]
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        _source = await RawInputMouseSource.ConnectAsync(cancellationToken).ConfigureAwait(false);
        _running = true;
        _lastError = null;
        HostingLog.RawInputMousePumpStarted(logger);
        _source.Run(HandleMouseInput, cancellationToken);
    }

    private void HandleMouseInput(in MouseInput input)
    {
        if (ShouldForwardRawInputMouse(in input))
        {
            broker.Send(in input);
        }
    }

    internal static bool ShouldForwardRawInputMouse(in MouseInput input)
    {
        // Steam Input-generated virtual mouse events arrive through Raw Input
        // without a source device handle/name. Physical and VIIPER loopback
        // events include source metadata, so the mouse route keeps only the
        // metadata-less events as the Steam mouse stream.
        return input.DeviceHandle == nint.Zero && string.IsNullOrWhiteSpace(input.DeviceName);
    }

    [SupportedOSPlatform("windows")]
    private void RunLinkedWindows(CancellationToken cancellationToken)
    {
        using CancellationTokenSource linked =
            CancellationTokenSource.CreateLinkedTokenSource(_stop.Token, cancellationToken);

        try
        {
            RunAsync(linked.Token).GetAwaiter().GetResult();
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException)
        {
            _running = false;
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            _running = false;
            _lastError = exception.Message;
            HostingLog.RawInputMousePumpStopped(logger, exception.Message);
        }
    }

    private static async Task IgnoreExpectedStopAsync(Task? task)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException)
        {
        }
    }
}
