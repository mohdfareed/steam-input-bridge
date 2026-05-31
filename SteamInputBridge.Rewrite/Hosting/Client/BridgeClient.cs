using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Diagnostics;
using StreamJsonRpc;

namespace SteamInputBridge.Hosting.Client;

/// <summary>Client run options.</summary>
public sealed record ClientRunOptions(string ProfileId);

/// <summary>Host-driven client runtime.</summary>
public sealed class BridgeClient(ClientRunOptions options, IHostApplicationLifetime lifetime, ILogger<BridgeClient> logger)
    : BackgroundService, IBridgeClientApi
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(1);

    // MARK: Lifecycle
    // ========================================================================

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

    // MARK: Control API
    // ========================================================================

    /// <inheritdoc />
    public Task ExitAsync()
    {
        BridgeLog.ClientExitRequested(logger, options.ProfileId);
        lifetime.StopApplication();
        return Task.CompletedTask;
    }

    // MARK: Implementation
    // ========================================================================

    private async Task RunConnectedAsync(CancellationToken cancellationToken)
    {
        BridgeLog.ClientConnecting(logger, IBridgeControlApi.Name);
        NamedPipeClientStream pipe = new(".", IBridgeControlApi.Name, PipeDirection.InOut, PipeOptions.Asynchronous);

        await using (pipe.ConfigureAwait(false))
        {
            await pipe.ConnectAsync((int)ConnectTimeout.TotalMilliseconds, cancellationToken).ConfigureAwait(false);
            using JsonRpc rpc = new(pipe);

            rpc.AddLocalRpcTarget<IBridgeClientApi>(this, null);
            IBridgeControlApi server = rpc.Attach<IBridgeControlApi>();
            rpc.StartListening();

            await server.ConnectAsync(Environment.ProcessId, options.ProfileId)
                .WaitAsync(ConnectTimeout, cancellationToken)
                .ConfigureAwait(false);

            BridgeLog.ClientConnected(logger, IBridgeControlApi.Name);
            await rpc.Completion.WaitAsync(cancellationToken).ConfigureAwait(false);
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
