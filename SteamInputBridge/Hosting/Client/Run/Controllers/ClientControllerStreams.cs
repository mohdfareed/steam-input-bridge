using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Hosting.Client.Connection;
using SteamInputBridge.Inputs.Sdl;

namespace SteamInputBridge.Hosting.Client.Run.Controllers;

internal interface IClientControllerStreams : IAsyncDisposable
{
    Task StartAsync(
        ClientService client,
        ClientRunLaunch launch,
        CancellationToken cancellationToken);
}

internal sealed class ClientControllerStreams : IClientControllerStreams
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);

    private readonly CancellationTokenSource _stop = new();
    private readonly ClientControllerSourceRegistry _sources = new();
    private readonly ClientControllerSourceRegistrar _registrar;
    private readonly ClientControllerPipeClient _pipe;
    private readonly ILogger _logger;
    private Task? _inputTask;

    public ClientControllerStreams(ILogger logger)
    {
        _logger = logger;
        _registrar = new ClientControllerSourceRegistrar(_sources, logger);
        _pipe = new ClientControllerPipeClient(_sources, _stop);
    }

    public async Task StartAsync(
        ClientService client,
        ClientRunLaunch launch,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(launch);

        await _pipe.ConnectAsync(launch.ControllerPipeName, cancellationToken).ConfigureAwait(false);
        _inputTask = Task.Run(() => RunInputLoopAsync(client, launch.ProfileId), CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        await _pipe.DisposeAsync().ConfigureAwait(false);
        await IgnoreExpectedStopAsync(_inputTask).ConfigureAwait(false);
        await _sources.DisposeAsync().ConfigureAwait(false);
        _registrar.Dispose();
        _stop.Dispose();
    }

    private async Task RunInputLoopAsync(ClientService client, string profileId)
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                IReadOnlyList<SdlGamepadSource> sources = await _registrar
                    .RefreshSourcesAsync(client, profileId, _stop.Token)
                    .ConfigureAwait(false);
                if (sources.Count == 0)
                {
                    await Task.Delay(RetryDelay, _stop.Token).ConfigureAwait(false);
                    continue;
                }

                SdlGamepadEventLoop.Run(
                    _sources.GetGamepadSourcesSnapshot,
                    _pipe.SendInput,
                    source => _registrar.RemoveSource(client, profileId, source, _stop.Token),
                    () => _registrar.RefreshSources(client, profileId, _stop.Token),
                    _stop.Token);
            }
            catch (Exception exception) when (
                exception is SdlGamepadDisconnectedException or
                    InvalidOperationException or
                    IOException or
                    TimeoutException or
                    ObjectDisposedException)
            {
                if (_stop.IsCancellationRequested)
                {
                    return;
                }

                HostingLog.SdlControllerStreamingRestarting(_logger, exception.Message);
                await Task.Delay(RetryDelay, _stop.Token).ConfigureAwait(false);
            }
        }
    }

    private static async Task IgnoreExpectedStopAsync(Task? task)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            // Shutdown and reconnect must not hang behind SDL/pipe teardown.
            // The loop has already been cancelled; if a native wait or broken
            // pipe ignores that briefly, the client run can still release.
            await task.WaitAsync(StopTimeout).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is OperationCanceledException or IOException or ObjectDisposedException or TimeoutException)
        {
        }
    }
}
