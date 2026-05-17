using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Hosting;

internal sealed class HostSingleInstance : IDisposable
{
    private Semaphore? _semaphore;

    private HostSingleInstance(Semaphore semaphore)
    {
        _semaphore = semaphore;
    }

    public static HostSingleInstance? TryAcquire(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Semaphore semaphore = new(initialCount: 1, maximumCount: 1, name);

        try
        {
            return semaphore.WaitOne(0)
                ? new HostSingleInstance(semaphore)
                : DisposeSemaphore(semaphore);
        }
        catch
        {
            semaphore.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        Semaphore? semaphore = Interlocked.Exchange(ref _semaphore, null);
        if (semaphore is null)
        {
            return;
        }

        try
        {
            _ = semaphore.Release();
        }
        catch (SemaphoreFullException)
        {
        }
        finally
        {
            semaphore.Dispose();
        }
    }

    private static HostSingleInstance? DisposeSemaphore(Semaphore semaphore)
    {
        semaphore.Dispose();
        return null;
    }
}

internal sealed class ForwardingHost(
    IForwardingRoute route,
    ILogger? logger = null,
    Func<bool>? shouldForward = null) : IAsyncDisposable
{
    private readonly IForwardingRoute _route = route ?? throw new ArgumentNullException(nameof(route));
    private int _enabledLeaseCount;

    public string RouteId => _route.RouteId;

    public bool IsEnabled => Volatile.Read(ref _enabledLeaseCount) > 0;

    public bool IsConnected => _route.IsConnected;

    public int EnabledLeaseCount => Volatile.Read(ref _enabledLeaseCount);

    public IDisposable Enable()
    {
        _ = Interlocked.Increment(ref _enabledLeaseCount);
        ForwardingHostLog.Enabled(logger, RouteId, EnabledLeaseCount);

        return new ForwardingHostEnableState(ReleaseEnable);
    }

    public void Run(CancellationToken cancellationToken = default)
    {
        ForwardingHostLog.Starting(logger, RouteId);

        try
        {
            _route.Run(() => IsEnabled && (shouldForward?.Invoke() ?? true), cancellationToken);
        }
        finally
        {
            ForwardingHostLog.Stopped(logger, RouteId);
        }
    }

    public ValueTask DisposeAsync()
    {
        return _route.DisposeAsync();
    }

    private void ReleaseEnable()
    {
        int remaining = Interlocked.Decrement(ref _enabledLeaseCount);
        if (remaining < 0)
        {
            _ = Interlocked.Exchange(ref _enabledLeaseCount, 0);
            throw new InvalidOperationException("Host enable leases are unbalanced.");
        }

        ForwardingHostLog.Released(logger, RouteId, remaining);
    }
}

internal sealed class ForwardingHostEnableState(Action release) : IDisposable
{
    private Action? _release = release;

    public void Dispose()
    {
        Action? capturedRelease = Interlocked.Exchange(ref _release, null);
        capturedRelease?.Invoke();
    }
}

internal static class ForwardingHostLog
{
    private static readonly Action<ILogger, string, int, Exception?> EnabledMessage =
        LoggerMessage.Define<string, int>(
            LogLevel.Information,
            new EventId(1, nameof(Enabled)),
            "Enabled route {RouteId}. Enabled clients: {EnabledClientCount}.");

    private static readonly Action<ILogger, string, Exception?> StartingMessage =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(2, nameof(Starting)),
            "Starting route {RouteId}.");

    private static readonly Action<ILogger, string, Exception?> StoppedMessage =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(3, nameof(Stopped)),
            "Stopped route {RouteId}.");

    private static readonly Action<ILogger, string, int, Exception?> ReleasedMessage =
        LoggerMessage.Define<string, int>(
            LogLevel.Information,
            new EventId(4, nameof(Released)),
            "Released route {RouteId}. Enabled clients: {EnabledClientCount}.");

    public static void Enabled(ILogger? logger, string routeId, int enabledClientCount)
    {
        if (logger is not null)
        {
            EnabledMessage(logger, routeId, enabledClientCount, null);
        }
    }

    public static void Starting(ILogger? logger, string routeId)
    {
        if (logger is not null)
        {
            StartingMessage(logger, routeId, null);
        }
    }

    public static void Stopped(ILogger? logger, string routeId)
    {
        if (logger is not null)
        {
            StoppedMessage(logger, routeId, null);
        }
    }

    public static void Released(ILogger? logger, string routeId, int enabledClientCount)
    {
        if (logger is not null)
        {
            ReleasedMessage(logger, routeId, enabledClientCount, null);
        }
    }
}
