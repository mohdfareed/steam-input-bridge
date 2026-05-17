using System;
using System.Text.Json;

namespace VirtualMouse.Hosting;

// MARK: Pipeline Messages
// ============================================================================

internal sealed record RequestMessage(Guid Id, string Method, JsonElement? Payload)
{
    internal T ReadPayload<T>()
    {
        if (Payload is not { } payload)
        {
            throw new InvalidOperationException("Request payload was empty.");
        }

        T? result = JsonSerializer.Deserialize<T>(payload.GetRawText(), RequestResponsePipe.JsonOptions);
        return result ?? throw new InvalidOperationException("Request payload was empty.");
    }
}

internal sealed record ResponseMessage(Guid Id, bool Success, JsonElement? Payload, string? Error);

// MARK: API Messages
// ============================================================================

internal sealed record ConnectRequest(int ProcessId);

internal sealed record ConnectResponse(Guid ClientId);

internal sealed record Ack;
