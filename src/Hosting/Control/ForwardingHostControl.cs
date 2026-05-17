using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Inputs.Sdl;
using Microsoft.Extensions.Logging;
using PolyType;
using StreamJsonRpc;

namespace Hosting;

/// <summary>Serves local forwarding control commands.</summary>
internal sealed class ForwardingHostServer(
    ForwardingHostRuntime runtime,
    string pipeName,
    Action? requestStop = null,
    ILogger? logger = null)
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        ForwardingHostControlLog.StartingServer(logger, pipeName);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
#pragma warning disable CA2000
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
        using CancellationTokenRegistration streamRegistration = cancellationToken.Register(static target =>
        {
            ((Stream)target!).Dispose();
        }, stream);

        ForwardingHostControlSession target = new(runtime, requestStop, logger);
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
    Task<ForwardingHostStatus> GetStatusAsync();

    Task EnableMouseAsync();

    Task DisableMouseAsync();

    Task SetEmulationEnabledAsync(bool enabled);

    Task<bool> ToggleEmulationEnabledAsync();

    Task SetPhysicalMotionEnabledAsync(bool enabled);

    Task<bool> TogglePhysicalMotionEnabledAsync();

    Task<GamepadReportSessionInfo> AttachSteamControllerAsync(SdlControllerInfo controller);

    Task StopAsync();
}

internal sealed class ForwardingHostControlSession(
    ForwardingHostRuntime runtime,
    Action? requestStop,
    ILogger? logger) : IForwardingHostControl, IDisposable
{
    private IDisposable? _mouseLease;
    private readonly List<Guid> _gamepadSessions = [];

    public Task<ForwardingHostStatus> GetStatusAsync()
    {
        ForwardingHostControlLog.ReceivedCommand(logger, nameof(GetStatusAsync));
        return runtime.GetStatusAsync();
    }

    public async Task EnableMouseAsync()
    {
        ForwardingHostControlLog.ReceivedCommand(logger, nameof(EnableMouseAsync));
        if (_mouseLease is not null)
        {
            return;
        }

        _mouseLease = await runtime.EnableMouseAsync(CancellationToken.None).ConfigureAwait(false);
        ForwardingHostControlLog.LeaseOpened(logger, ForwardingRouteIds.Mouse);
    }

    public Task DisableMouseAsync()
    {
        ForwardingHostControlLog.ReceivedCommand(logger, nameof(DisableMouseAsync));
        ReleaseMouseLease();
        return Task.CompletedTask;
    }

    public Task SetEmulationEnabledAsync(bool enabled)
    {
        ForwardingHostControlLog.ReceivedCommand(logger, nameof(SetEmulationEnabledAsync));
        return runtime.SetEmulationEnabledAsync(enabled);
    }

    public Task<bool> ToggleEmulationEnabledAsync()
    {
        ForwardingHostControlLog.ReceivedCommand(logger, nameof(ToggleEmulationEnabledAsync));
        return runtime.ToggleEmulationEnabledAsync();
    }

    public Task SetPhysicalMotionEnabledAsync(bool enabled)
    {
        ForwardingHostControlLog.ReceivedCommand(logger, nameof(SetPhysicalMotionEnabledAsync));
        return runtime.SetPhysicalMotionEnabledAsync(enabled);
    }

    public Task<bool> TogglePhysicalMotionEnabledAsync()
    {
        ForwardingHostControlLog.ReceivedCommand(logger, nameof(TogglePhysicalMotionEnabledAsync));
        return runtime.TogglePhysicalMotionEnabledAsync();
    }

    public async Task<GamepadReportSessionInfo> AttachSteamControllerAsync(SdlControllerInfo controller)
    {
        ForwardingHostControlLog.ReceivedCommand(logger, nameof(AttachSteamControllerAsync));
        GamepadReportSessionInfo session = await runtime
            .AttachSteamControllerAsync(controller, CancellationToken.None)
            .ConfigureAwait(false);
        _gamepadSessions.Add(session.SessionId);
        return session;
    }

    public Task StopAsync()
    {
        ForwardingHostControlLog.ReceivedCommand(logger, nameof(StopAsync));
        requestStop?.Invoke();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        ReleaseMouseLease();
        foreach (Guid sessionId in _gamepadSessions)
        {
            runtime.DetachSteamControllerAsync(sessionId).GetAwaiter().GetResult();
        }

        _gamepadSessions.Clear();
    }

    private void ReleaseMouseLease()
    {
        IDisposable? lease = Interlocked.Exchange(ref _mouseLease, null);
        if (lease is not null)
        {
            lease.Dispose();
            ForwardingHostControlLog.LeaseClosed(logger, ForwardingRouteIds.Mouse);
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
