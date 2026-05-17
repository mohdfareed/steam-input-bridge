using System;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using StreamJsonRpc;

namespace Hosting;

/// <summary>Status reported by the local forwarding server.</summary>
/// <param name="RouteId">Hosted route id.</param>
/// <param name="IsEnabled">Whether forwarding is enabled.</param>
/// <param name="IsConnected">Whether route input and output are connected.</param>
/// <param name="EnabledClientCount">Number of connected enabled clients.</param>
public readonly record struct ForwardingStatus(
    string RouteId,
    bool IsEnabled,
    bool IsConnected,
    int EnabledClientCount);

/// <summary>Local forwarding client options.</summary>
public sealed record ForwardingClientOptions
{
    /// <summary>Route to control.</summary>
    public ForwardingRouteKind Route { get; init; }

    /// <summary>Connection timeout.</summary>
    public TimeSpan? ConnectTimeout { get; init; }
}

/// <summary>Controls a local forwarding server.</summary>
public sealed class ForwardingClient
{
    private static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(2);
    private readonly string _pipeName;
    private readonly TimeSpan? _connectTimeout;

    /// <summary>Creates a client for a route.</summary>
    public ForwardingClient(ForwardingRouteKind route = ForwardingRouteKind.Mouse, TimeSpan? connectTimeout = null)
        : this(new ForwardingClientOptions { Route = route, ConnectTimeout = connectTimeout })
    {
    }

    /// <summary>Creates a client from options.</summary>
    public ForwardingClient(ForwardingClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _pipeName = ForwardingServer.GetPipeName(options.Route);
        _connectTimeout = options.ConnectTimeout;
    }

    internal ForwardingClient(string pipeName, TimeSpan? connectTimeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        _pipeName = pipeName;
        _connectTimeout = connectTimeout;
    }

    /// <summary>Enables forwarding until the returned lease is disposed.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ForwardingEnableLease> EnableAsync(CancellationToken cancellationToken = default)
    {
        NamedPipeClientStream pipe = CreatePipe();
        try
        {
            await ConnectAsync(pipe, cancellationToken).ConfigureAwait(false);
            IForwardingHostControl proxy = JsonRpc.Attach<IForwardingHostControl>(pipe);
            await proxy.EnableAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            return new ForwardingEnableLease(pipe, proxy);
        }
        catch
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Gets host status.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ForwardingStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(_pipeName);

        using NamedPipeClientStream pipe = CreatePipe();

        await ConnectAsync(pipe, cancellationToken).ConfigureAwait(false);
        IForwardingHostControl proxy = JsonRpc.Attach<IForwardingHostControl>(pipe);
        using IDisposable proxyHandle = (IDisposable)proxy;
        return await proxy.GetStatusAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Requests the host to stop.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(_pipeName);

        using NamedPipeClientStream pipe = CreatePipe();

        await ConnectAsync(pipe, cancellationToken).ConfigureAwait(false);
        IForwardingHostControl proxy = JsonRpc.Attach<IForwardingHostControl>(pipe);
        using IDisposable proxyHandle = (IDisposable)proxy;
        await proxy.StopAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private NamedPipeClientStream CreatePipe()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(_pipeName);

        return new NamedPipeClientStream(
            ".",
            _pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
    }

    private async Task ConnectAsync(NamedPipeClientStream pipe, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_connectTimeout ?? DefaultConnectTimeout);

        try
        {
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Timed out connecting to the host control pipe.");
        }
    }
}

/// <summary>Keeps forwarding enabled while connected.</summary>
public sealed class ForwardingEnableLease : IAsyncDisposable, IDisposable
{
    private NamedPipeClientStream? _pipe;
    private IDisposable? _proxy;

    internal ForwardingEnableLease(
        NamedPipeClientStream pipe,
        IForwardingHostControl proxy)
    {
        _pipe = pipe;
        _proxy = (IDisposable)proxy;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        NamedPipeClientStream? pipe = Interlocked.Exchange(ref _pipe, null);
        IDisposable? proxy = Interlocked.Exchange(ref _proxy, null);
        proxy?.Dispose();
        pipe?.Dispose();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        NamedPipeClientStream? pipe = Interlocked.Exchange(ref _pipe, null);
        IDisposable? proxy = Interlocked.Exchange(ref _proxy, null);
        proxy?.Dispose();

        if (pipe is not null)
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
        }
    }
}
