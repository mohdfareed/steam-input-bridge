using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VirtualMouse.Hosting;

// One JSON line is one request or response. Events can be added later without
// changing the client/server object model.
internal sealed class RequestResponsePipe(Stream stream) : IAsyncDisposable
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly StreamReader _reader = new(stream, leaveOpen: true);
    private readonly StreamWriter _writer = new(stream, leaveOpen: true) { AutoFlush = true };
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    // MARK: API
    // ============================================================================

    internal async Task<TResponse> SendAsync<TRequest, TResponse>(
        string method,
        TRequest request,
        CancellationToken cancellationToken)
    {
        Guid id = Guid.NewGuid();
        await WriteAsync(
                new RequestMessage(id, method, JsonSerializer.SerializeToElement(request, JsonOptions)),
                cancellationToken)
            .ConfigureAwait(false);

        ResponseMessage response = await ReadResponseAsync(cancellationToken).ConfigureAwait(false);
        return ReadResponse<TResponse>(id, response);
    }

    internal async Task<TResponse> SendAsync<TResponse>(string method, CancellationToken cancellationToken)
    {
        Guid id = Guid.NewGuid();
        await WriteAsync(new RequestMessage(id, method, null), cancellationToken).ConfigureAwait(false);

        ResponseMessage response = await ReadResponseAsync(cancellationToken).ConfigureAwait(false);
        return ReadResponse<TResponse>(id, response);
    }

    internal async Task<RequestMessage?> ReadRequestAsync(CancellationToken cancellationToken)
    {
        string? line = await _reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        return line is null
            ? null
            : JsonSerializer.Deserialize<RequestMessage>(line, JsonOptions);
    }

    internal Task SendResponseAsync<TResponse>(Guid requestId, TResponse response, CancellationToken cancellationToken)
    {
        return WriteAsync(
            new ResponseMessage(requestId, true, JsonSerializer.SerializeToElement(response, JsonOptions), null),
            cancellationToken);
    }

    internal Task SendErrorAsync(Guid requestId, string error, CancellationToken cancellationToken)
    {
        return WriteAsync(new ResponseMessage(requestId, false, null, error), cancellationToken);
    }

    internal async ValueTask DisposeAsync()
    {
        _writeGate.Dispose();
        try
        {
            await _writer.DisposeAsync().ConfigureAwait(false);
        }
        catch (IOException)
        {
            // A broken pipe is a normal disconnect path; cleanup should not fail.
        }
        catch (ObjectDisposedException)
        {
        }

        _reader.Dispose();
    }

    ValueTask IAsyncDisposable.DisposeAsync()
    {
        return DisposeAsync();
    }

    // MARK: Helpers
    // ============================================================================

    private async Task<ResponseMessage> ReadResponseAsync(CancellationToken cancellationToken)
    {
        string? line = await _reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        return line is null
            ? throw new EndOfStreamException("Server disconnected.")
            : JsonSerializer.Deserialize<ResponseMessage>(line, JsonOptions) ??
                throw new InvalidOperationException("Invalid server response.");
    }

    private static TResponse ReadResponse<TResponse>(Guid requestId, ResponseMessage response)
    {
        if (response.Id != requestId)
        {
            throw new InvalidOperationException("Received a response for a different request.");
        }

        if (!response.Success)
        {
            throw new InvalidOperationException(response.Error ?? "Server request failed.");
        }

        if (response.Payload is not { } payload)
        {
            throw new InvalidOperationException("Server response was empty.");
        }

        TResponse? result = JsonSerializer.Deserialize<TResponse>(payload.GetRawText(), JsonOptions);
        return result ?? throw new InvalidOperationException("Server response was empty.");
    }

    private async Task WriteAsync<T>(T message, CancellationToken cancellationToken)
    {
        string line = JsonSerializer.Serialize(message, JsonOptions);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ = _writeGate.Release();
        }
    }
}
