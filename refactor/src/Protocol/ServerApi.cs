using System;
using System.Threading;
using System.Threading.Tasks;

namespace VirtualMouse.Protocol;

// MARK: Requests
// ============================================================================

public static class ServerApi
{
    private const string ConnectMethod = "connect";
    private const string AckMethod = "ack";

    public static Task<ConnectResponse> ConnectAsync(
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

    public static Task<Ack> AckAsync(
        this RequestResponsePipe pipe,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pipe);

        return pipe.SendAsync<Ack>(AckMethod, cancellationToken);
    }

    public static bool IsConnect(RequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.Method == ConnectMethod;
    }

    public static bool IsAck(RequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.Method == AckMethod;
    }
}
