using System;
using System.Text.Json;

namespace VirtualMouse.Protocol;

// MARK: Pipeline Messages
// ========================================================================

public sealed record RequestMessage(Guid Id, string Method, JsonElement? Payload)
{
    public T ReadPayload<T>()
    {
        if (Payload is not { } payload)
        {
            throw new InvalidOperationException("Request payload was empty.");
        }

        T? result = JsonSerializer.Deserialize<T>(payload.GetRawText(), RequestResponsePipe.JsonOptions);
        return result ?? throw new InvalidOperationException("Request payload was empty.");
    }
}

public sealed record ResponseMessage(Guid Id, bool Success, JsonElement? Payload, string? Error);

// MARK: API Messages
// ========================================================================

public sealed record ConnectRequest(int ProcessId);

public sealed record ConnectResponse(Guid ClientId);

public sealed record Ack;
