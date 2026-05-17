using System;
using System.Threading;
using System.Threading.Tasks;

namespace VirtualMouse.Hosting;

// MARK: Requests
// ============================================================================

internal static class ServerApi
{
    private const string ConnectMethod = "connect";
    private const string AckMethod = "ack";

    internal static Task<ConnectResponse> ConnectAsync(
        this RequestResponsePipe pipe,
        int processId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pipe);

        return pipe.SendAsync<ConnectRequest, ConnectResponse>(
            ConnectMethod,
            new ConnectRequest(processId),
            cancellationToken);
    }

    internal static Task<Ack> AckAsync(
        this RequestResponsePipe pipe,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pipe);

        return pipe.SendAsync<Ack>(AckMethod, cancellationToken);
    }

    internal static bool IsConnect(RequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.Method == ConnectMethod;
    }

    internal static bool IsAck(RequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.Method == AckMethod;
    }
}
