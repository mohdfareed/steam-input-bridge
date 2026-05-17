using System;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using StreamJsonRpc;

namespace Hosting;

/// <summary>Local forwarding client options.</summary>
public sealed record ForwardingClientOptions
{
    /// <summary>Connection timeout.</summary>
    public TimeSpan? ConnectTimeout { get; init; }
}

/// <summary>Controls a local forwarding server.</summary>
public sealed class ForwardingClient
{
    private static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(2);
    private readonly TimeSpan? _connectTimeout;

    /// <summary>Creates a client with the default host pipe.</summary>
    public ForwardingClient(TimeSpan? connectTimeout = null)
        : this(new ForwardingClientOptions { ConnectTimeout = connectTimeout })
    {
    }

    /// <summary>Creates a client from options.</summary>
    public ForwardingClient(ForwardingClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _connectTimeout = options.ConnectTimeout;
    }

    internal ForwardingClient(string pipeName, TimeSpan? connectTimeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        PipeName = pipeName;
        _connectTimeout = connectTimeout;
    }

    private string PipeName { get; } = ForwardingServer.PipeName;

    /// <summary>Enables forwarding for a route until the returned lease is disposed.</summary>
    public async Task<ForwardingEnableLease> EnableAsync(
        ForwardingRouteKind route,
        CancellationToken cancellationToken = default)
    {
        NamedPipeClientStream pipe = CreatePipe();

        try
        {
            await ConnectAsync(pipe, cancellationToken).ConfigureAwait(false);
            IForwardingHostControl proxy = JsonRpc.Attach<IForwardingHostControl>(pipe);
            await proxy.EnableAsync(route).WaitAsync(cancellationToken).ConfigureAwait(false);
            return new ForwardingEnableLease(pipe, proxy);
        }
        catch
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Gets host status.</summary>
    public async Task<ForwardingHostStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        using NamedPipeClientStream pipe = CreatePipe();

        await ConnectAsync(pipe, cancellationToken).ConfigureAwait(false);
        IForwardingHostControl proxy = JsonRpc.Attach<IForwardingHostControl>(pipe);
        using IDisposable proxyHandle = (IDisposable)proxy;
        return await proxy.GetStatusAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Requests the host to stop.</summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        using NamedPipeClientStream pipe = CreatePipe();

        await ConnectAsync(pipe, cancellationToken).ConfigureAwait(false);
        IForwardingHostControl proxy = JsonRpc.Attach<IForwardingHostControl>(pipe);
        using IDisposable proxyHandle = (IDisposable)proxy;
        await proxy.StopAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private NamedPipeClientStream CreatePipe()
    {
        return new NamedPipeClientStream(
            ".",
            PipeName,
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
