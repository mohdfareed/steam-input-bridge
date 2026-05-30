using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Diagnostics;
using SteamInputBridge.Hosting.Control;
using SteamInputBridge.Hosting.Server;
using StreamJsonRpc;

namespace SteamInputBridge.Hosting.Client;

/// <summary>Client run options.</summary>
public sealed record ClientRunOptions(string ProfileId);

/// <summary>Host-driven client runtime.</summary>
public sealed class BridgeClient(ClientRunOptions options, ILogger<BridgeClient> logger) : BackgroundService
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(1);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        BridgeLog.ClientStarted(logger, options.ProfileId);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunConnectedAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (IsConnectionFailure(exception))
                {
                    BridgeLog.ClientConnectionFailed(logger, exception.Message);
                }

                await Task.Delay(ReconnectDelay, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            BridgeLog.ClientStopped(logger, options.ProfileId);
        }
    }

    private async Task RunConnectedAsync(CancellationToken cancellationToken)
    {
        BridgeLog.ClientConnecting(logger, BridgePipeNames.Control);
        NamedPipeClientStream pipe = new(".", BridgePipeNames.Control, PipeDirection.InOut, PipeOptions.Asynchronous);

        await using (pipe.ConfigureAwait(false))
        {
            await pipe.ConnectAsync((int)ConnectTimeout.TotalMilliseconds, cancellationToken).ConfigureAwait(false);
            IBridgeControlApi server = JsonRpc.Attach<IBridgeControlApi>(pipe);
            await server.ConnectAsync(Environment.ProcessId, options.ProfileId)
                .WaitAsync(ConnectTimeout, cancellationToken)
                .ConfigureAwait(false);

            BridgeLog.ClientConnected(logger, BridgePipeNames.Control);
            while (pipe.IsConnected && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static bool IsConnectionFailure(Exception exception)
    {
        return exception is IOException
            or EndOfStreamException
            or InvalidOperationException
            or TimeoutException
            or ConnectionLostException
            or ObjectDisposedException;
    }
}
