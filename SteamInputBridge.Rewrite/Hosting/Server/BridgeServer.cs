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

internal sealed class BridgeControlSession(BridgeService service, Guid connectionId) : IBridgeControlApi
{
    /// <inheritdoc />
    public Task ConnectAsync(int processId, string profileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        _ = service.RegisterClient(connectionId, processId, profileId);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<BridgeServerStatus> GetStatusAsync()
    {
        return Task.FromResult(service.Status);
    }
}

/// <summary>Host-driven server runtime.</summary>
public sealed class BridgeServer(SettingsService settings, BridgeService service, ILogger<BridgeServer> logger)
    : BackgroundService
{
    private readonly ConcurrentBag<NamedPipeServerStream> _pipes = [];

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

    private void OnSettingsChanged(object? sender, ApplicationSettingsChangedEventArgs args)
    {
        _ = sender;
        BridgeLog.ServerSettingsApplied(logger, args.Settings);
    }

    private async Task RunClientAsync(NamedPipeServerStream pipe)
    {
        Guid connectionId = Guid.NewGuid();
        BridgeControlSession session = new(service, connectionId);
        await using (pipe.ConfigureAwait(false))
        {
            try
            {
                using JsonRpc rpc = JsonRpc.Attach(pipe, session);
                await rpc.Completion.ConfigureAwait(false);
            }
            catch (Exception exception) when (IsClientDisconnect(exception))
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
}
