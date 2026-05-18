using System;
using System.ComponentModel;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VirtualMouse.Forwarding;
using VirtualMouse.Inputs.RawInput;

namespace VirtualMouse.Hosting;

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
            logger.LogWarning("Raw Input mouse pump disabled: Windows is required.");
            return;
        }

#pragma warning disable CA1416 // Guarded by the Windows check above.
        _task = Task.Run(() => RunLinkedWindows(cancellationToken), CancellationToken.None);
#pragma warning restore CA1416
    }

    public MouseInputPumpStatus GetStatus()
    {
        return new MouseInputPumpStatus(
            _running,
            OperatingSystem.IsWindows() && _source is not null && IsSourceConnected(_source),
            _lastError);
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
            await DisposeSourceAsync(_source).ConfigureAwait(false);
        }

        _stop.Dispose();
    }

    [SupportedOSPlatform("windows")]
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        _source = await RawInputMouseSource.ConnectAsync(cancellationToken).ConfigureAwait(false);
        _running = true;
        _lastError = null;
        logger.LogInformation("Raw Input mouse pump started.");
        _source.Run(HandleMouseInput, cancellationToken);
    }

    private void HandleMouseInput(in MouseInput input)
    {
        broker.Send(in input);
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
            logger.LogWarning("Raw Input mouse pump stopped: {Message}", exception.Message);
        }
    }

    [SupportedOSPlatform("windows")]
    private static ValueTask DisposeSourceAsync(RawInputMouseSource source)
    {
        return source.DisposeAsync();
    }

    [SupportedOSPlatform("windows")]
    private static bool IsSourceConnected(RawInputMouseSource source)
    {
        return source.IsConnected;
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
