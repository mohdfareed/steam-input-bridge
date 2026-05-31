using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Diagnostics;
using SteamInputBridge.Settings;
using StreamJsonRpc;

namespace SteamInputBridge.Hosting.Server;

/// <summary>Host-driven server runtime.</summary>
public sealed class BridgeServer(SettingsService settings, BridgeService service, ILogger<BridgeServer> logger)
    : BackgroundService
{
    private const string ServerSemaphoreName = @"Local\SteamInputBridge.Server";

    private readonly ConcurrentBag<NamedPipeServerStream> _pipes = [];
    private Semaphore? _serverInstance;

    // MARK: Lifecycle
    // ========================================================================

    /// <inheritdoc />
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _serverInstance = AcquireServerInstance();
        return base.StartAsync(cancellationToken);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        settings.Changed += OnSettingsChanged;
        BridgeLog.ServerStarted(logger, settings.Current);

        try
        {
            BridgeLog.ServerListening(logger, IBridgeControlApi.Name);
            while (!stoppingToken.IsCancellationRequested)
            {
                NamedPipeServerStream? pipe = null;

                try
                {
                    pipe = new NamedPipeServerStream(
                        IBridgeControlApi.Name,
                        PipeDirection.InOut,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);
                    await pipe.WaitForConnectionAsync(stoppingToken).ConfigureAwait(false);

                    _pipes.Add(pipe);
                    _ = RunClientAsync(pipe);
                    pipe = null;
                }
                finally
                {
                    if (pipe is not null)
                    {
                        await pipe.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            settings.Changed -= OnSettingsChanged;

            _ = _serverInstance?.Release();
            _serverInstance?.Dispose();
            _serverInstance = null;

            BridgeLog.ServerStopped(logger);
        }
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (NamedPipeServerStream pipe in _pipes)
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
        }

        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _serverInstance?.Dispose();
        base.Dispose();
    }

    // MARK: Connections
    // ========================================================================

    private void OnSettingsChanged(object? sender, ApplicationSettingsChangedEventArgs args)
    {
        _ = sender;
        BridgeLog.ServerSettingsApplied(logger, args.Settings);
    }

    private async Task RunClientAsync(NamedPipeServerStream pipe)
    {
        Guid connectionId = Guid.NewGuid();
        await using (pipe.ConfigureAwait(false))
        {
            try
            {
                using JsonRpc rpc = new(pipe);
                IBridgeClientApi client = rpc.Attach<IBridgeClientApi>();
                BridgeControlSession session = new(service, connectionId, client, logger);

                rpc.AddLocalRpcTarget<IBridgeControlApi>(session, null);
                rpc.StartListening();
                await rpc.Completion.ConfigureAwait(false);
            }
            catch (Exception exception) when (IsClientDisconnect(exception) || exception is InvalidOperationException)
            {
                BridgeLog.ClientControlPipeClosed(logger, exception.Message);
            }
            finally
            {
                ConnectedClient? client = service.UnregisterClient(connectionId);
                if (client is not null)
                {
                    BridgeLog.ClientDisconnected(logger, client.ProcessId, client.ProfileId);
                }
            }
        }
    }

    private static bool IsClientDisconnect(Exception exception)
    {
        return exception is IOException or ObjectDisposedException or ConnectionLostException;
    }

    // MARK: Instance Guard
    // ========================================================================

    private static Semaphore AcquireServerInstance()
    {
        Semaphore serverInstance = new(initialCount: 1, maximumCount: 1, ServerSemaphoreName);
        if (serverInstance.WaitOne(TimeSpan.Zero))
        {
            return serverInstance;
        }

        serverInstance.Dispose();
        throw new ServerAlreadyRunningException();
    }
}
