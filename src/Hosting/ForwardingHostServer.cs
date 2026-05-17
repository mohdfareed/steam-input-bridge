using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PolyType;
using StreamJsonRpc;

namespace Hosting;

/// <summary>Serves local forwarding control commands.</summary>
internal sealed class ForwardingHostServer(
    ForwardingHost host,
    string pipeName,
    ILogger? logger = null)
{
    /// <summary>Runs the control server until cancelled.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        ForwardingHostControlLog.StartingServer(logger, pipeName);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
#pragma warning disable CA2000 // Ownership transfers to the client handling task.
            NamedPipeServerStream pipe = CreatePipe();
#pragma warning restore CA2000
            using CancellationTokenRegistration registration = cancellationToken.Register(static target =>
            {
                ((NamedPipeServerStream)target!).Dispose();
            }, pipe);

            try
            {
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            NamedPipeServerStream connectedPipe = pipe;
            pipe = null!;
            _ = Task.Run(async () =>
            {
                try
                {
                    using (connectedPipe)
                    {
                        await HandleConnectionAsync(connectedPipe, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                }
                catch (IOException exception)
                {
                    ForwardingHostControlLog.ConnectionClosed(logger, exception);
                }
            }, CancellationToken.None);
        }
    }

    private NamedPipeServerStream CreatePipe()
    {
        return new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 254,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
    }

    private async Task HandleConnectionAsync(Stream stream, CancellationToken cancellationToken)
    {
        using CancellationTokenRegistration registration = cancellationToken.Register(static target =>
        {
            ((Stream)target!).Dispose();
        }, stream);

        ForwardingHostControlSession target = new(host, logger);
        using JsonRpc rpc = JsonRpc.Attach(stream, target);

        try
        {
            await rpc.Completion.ConfigureAwait(false);
        }
        finally
        {
            target.Dispose();
        }
    }
}

[JsonRpcContract]
[GenerateShape(IncludeMethods = MethodShapeFlags.PublicInstance)]
internal partial interface IForwardingHostControl
{
    Task<ForwardingStatus> GetStatusAsync();

    Task EnableAsync();
}

internal sealed class ForwardingHostControlSession(
    ForwardingHost host,
    ILogger? logger) : IForwardingHostControl, IDisposable
{
    private IDisposable? _lease;

    public Task<ForwardingStatus> GetStatusAsync()
    {
        ForwardingHostControlLog.ReceivedCommand(logger, nameof(GetStatusAsync));
        return Task.FromResult(new ForwardingStatus(
            host.RouteId,
            host.IsEnabled,
            host.IsConnected,
            host.EnabledLeaseCount));
    }

    public Task EnableAsync()
    {
        ForwardingHostControlLog.ReceivedCommand(logger, nameof(EnableAsync));
        if (_lease is null)
        {
            _lease = host.Enable();
            ForwardingHostControlLog.LeaseOpened(logger, host.RouteId);
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        IDisposable? lease = _lease;
        _lease = null;
        if (lease is not null)
        {
            lease.Dispose();
            ForwardingHostControlLog.LeaseClosed(logger, host.RouteId);
        }
    }
}

internal static class ForwardingHostControlLog
{
    private static readonly Action<ILogger, string, Exception?> StartingServerMessage =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(1, nameof(StartingServer)),
            "Starting host control server on pipe {PipeName}.");

    private static readonly Action<ILogger, Exception?> ConnectionClosedMessage =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(2, nameof(ConnectionClosed)),
            "Host control connection closed.");

    private static readonly Action<ILogger, string, Exception?> ReceivedCommandMessage =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(3, nameof(ReceivedCommand)),
            "Received host control command {Command}.");

    private static readonly Action<ILogger, string, Exception?> LeaseOpenedMessage =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(4, nameof(LeaseOpened)),
            "Host enable lease opened for route {RouteId}.");

    private static readonly Action<ILogger, string, Exception?> LeaseClosedMessage =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(5, nameof(LeaseClosed)),
            "Host enable lease closed for route {RouteId}.");

    public static void StartingServer(ILogger? logger, string pipeName)
    {
        if (logger is not null)
        {
            StartingServerMessage(logger, pipeName, null);
        }
    }

    public static void ConnectionClosed(ILogger? logger, Exception exception)
    {
        if (logger is not null)
        {
            ConnectionClosedMessage(logger, exception);
        }
    }

    public static void ReceivedCommand(ILogger? logger, string command)
    {
        if (logger is not null)
        {
            ReceivedCommandMessage(logger, command, null);
        }
    }

    public static void LeaseOpened(ILogger? logger, string routeId)
    {
        if (logger is not null)
        {
            LeaseOpenedMessage(logger, routeId, null);
        }
    }

    public static void LeaseClosed(ILogger? logger, string routeId)
    {
        if (logger is not null)
        {
            LeaseClosedMessage(logger, routeId, null);
        }
    }
}
