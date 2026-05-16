using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

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
    private static readonly Encoding PipeEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
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
            StreamWriter writer = new(pipe, PipeEncoding, leaveOpen: true)
            {
                NewLine = "\n",
                AutoFlush = true,
            };
            StreamReader reader = new(pipe, PipeEncoding, detectEncodingFromByteOrderMarks: false, leaveOpen: true);

            await writer.WriteLineAsync("ENABLE".AsMemory(), cancellationToken).ConfigureAwait(false);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            string response = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) ??
                throw new IOException("Host closed the control pipe without a response.");

            return response.StartsWith("ERR ", StringComparison.Ordinal)
                ? throw new InvalidOperationException(response[4..])
                : response == "OK enabled"
                ? new ForwardingEnableLease(pipe, reader, writer)
                : throw new IOException("Host returned an invalid enable response.");
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
        string response = await SendCommandAsync("STATUS", cancellationToken).ConfigureAwait(false);
        return ForwardingHostControlProtocol.ParseStatus(response);
    }

    private async Task<string> SendCommandAsync(string command, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(_pipeName);

        using NamedPipeClientStream pipe = CreatePipe();

        await ConnectAsync(pipe, cancellationToken).ConfigureAwait(false);
        using StreamWriter writer = new(pipe, PipeEncoding, leaveOpen: true)
        {
            NewLine = "\n",
            AutoFlush = true,
        };
        using StreamReader reader = new(pipe, PipeEncoding, detectEncodingFromByteOrderMarks: false, leaveOpen: true);

        await writer.WriteLineAsync(command.AsMemory(), cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        string response = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) ??
            throw new IOException("Host closed the control pipe without a response.");

        return response.StartsWith("ERR ", StringComparison.Ordinal)
            ? throw new InvalidOperationException(response[4..])
            : response;
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
    private StreamReader? _reader;
    private StreamWriter? _writer;

    internal ForwardingEnableLease(
        NamedPipeClientStream pipe,
        StreamReader reader,
        StreamWriter writer)
    {
        _pipe = pipe;
        _reader = reader;
        _writer = writer;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        NamedPipeClientStream? pipe = Interlocked.Exchange(ref _pipe, null);
        StreamReader? reader = Interlocked.Exchange(ref _reader, null);
        StreamWriter? writer = Interlocked.Exchange(ref _writer, null);
        reader?.Dispose();
        writer?.Dispose();
        pipe?.Dispose();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        NamedPipeClientStream? pipe = Interlocked.Exchange(ref _pipe, null);
        StreamReader? reader = Interlocked.Exchange(ref _reader, null);
        StreamWriter? writer = Interlocked.Exchange(ref _writer, null);
        reader?.Dispose();
        if (writer is not null)
        {
            await writer.DisposeAsync().ConfigureAwait(false);
        }

        if (pipe is not null)
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
        }
    }
}
