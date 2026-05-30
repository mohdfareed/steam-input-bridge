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

/// <summary>Named pipe names used by the bridge host.</summary>
internal static class BridgePipeNames
{
    public const string Control = "SteamInputBridge";
}

/// <summary>Host-driven server runtime.</summary>
public sealed class BridgeServer(
    SettingsService settings,
    ILogger<BridgeServer> logger,
    ILogger<BridgeControlService> controlLogger) : BackgroundService
{
    private readonly ConcurrentBag<NamedPipeServerStream> _pipes = [];

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        settings.Changed += OnSettingsChanged;
        BridgeLog.ServerStarted(logger, settings.Current);

        try
        {
            BridgeLog.ServerListening(logger, BridgePipeNames.Control);
            while (!stoppingToken.IsCancellationRequested)
            {
                NamedPipeServerStream? pipe = null;
                try
                {
                    pipe = new NamedPipeServerStream(
                        BridgePipeNames.Control,
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
        BridgeControlService control = new(controlLogger);
        await using (pipe.ConfigureAwait(false))
        {
            try
            {
                using JsonRpc rpc = JsonRpc.Attach(pipe, control);
                await rpc.Completion.ConfigureAwait(false);
            }
            catch (Exception exception) when (IsClientDisconnect(exception))
            {
                BridgeLog.ClientControlPipeClosed(logger, exception.Message);
            }
            finally
            {
                if (control.Client is ConnectedClient client)
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
