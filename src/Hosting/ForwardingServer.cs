using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Outputs.Viiper;

namespace Hosting;

/// <summary>Per-route status reported by the local forwarding server.</summary>
/// <param name="RouteId">Hosted route id.</param>
/// <param name="IsConnected">Whether route input and output are connected.</param>
/// <param name="EnabledClientCount">Number of connected enabled clients.</param>
public readonly record struct ForwardingRouteStatus(
    string RouteId,
    bool IsConnected,
    int EnabledClientCount);

/// <summary>Host status reported by the local forwarding server.</summary>
/// <param name="Mouse">Mouse route status.</param>
/// <param name="Gamepads">Server-owned gamepad controller slots.</param>
/// <param name="EmulationEnabled">Whether emulation reports are currently forwarded.</param>
/// <param name="PhysicalMotionEnabled">Whether physical motion data is currently forwarded.</param>
public readonly record struct ForwardingHostStatus(
    ForwardingRouteStatus Mouse,
    IReadOnlyList<GamepadControllerSlotStatus> Gamepads,
    bool EmulationEnabled,
    bool PhysicalMotionEnabled);

/// <summary>Local forwarding server options.</summary>
public sealed record ForwardingServerOptions
{
    /// <summary>VIIPER connection options.</summary>
    public required ViiperOptions Viiper { get; init; }

    /// <summary>Lifecycle logger.</summary>
    public ILogger? Logger { get; init; }
}

/// <summary>Runs a local forwarding server.</summary>
public sealed class ForwardingServer(ForwardingServerOptions options) : IHostedService, IAsyncDisposable
{
    /// <summary>Host control pipe name.</summary>
    public const string PipeName = "Hosting";

    /// <summary>Host single-instance ownership name.</summary>
    public const string OwnershipName = @"Local\Hosting";

    private readonly ForwardingServerOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private CancellationTokenSource? _runCancellation;
    private Task? _runTask;

    /// <summary>Creates a server from configured options.</summary>
    public ForwardingServer(IOptions<ForwardingServerOptions> options)
        : this((options ?? throw new ArgumentNullException(nameof(options))).Value)
    {
    }

    /// <summary>Starts the server in the background.</summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_runTask is not null)
        {
            throw new InvalidOperationException("Forwarding server is already running.");
        }

        _runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _runTask = RunCoreAsync(_options, _runCancellation.Token);
        return _runTask.IsCompleted ? _runTask : Task.CompletedTask;
    }

    /// <summary>Stops a background server started with <see cref="StartAsync" />.</summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Task? task = _runTask;
        if (task is null)
        {
            return;
        }

        if (_runCancellation is not null)
        {
            await _runCancellation.CancelAsync().ConfigureAwait(false);
        }

        try
        {
            await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_runCancellation?.IsCancellationRequested == true)
        {
        }
        finally
        {
            _runTask = null;
            _runCancellation?.Dispose();
            _runCancellation = null;
        }
    }

    /// <summary>Runs a local host until cancelled.</summary>
    public Task RunAsync(CancellationToken cancellationToken = default)
    {
        return RunCoreAsync(_options, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private static async Task RunCoreAsync(
        ForwardingServerOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Viiper);
        using HostSingleInstance instance = HostSingleInstance.TryAcquire(OwnershipName) ??
            throw new InvalidOperationException("Another forwarding host is already running.");

        ForwardingHostRuntime runtime = ForwardingHostRuntimeFactory.Create(options);

        try
        {
            using CancellationTokenSource runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            ForwardingHostServer server = new(
                runtime,
                PipeName,
                () => runCancellation.Cancel(),
                options.Logger);

            await server.RunAsync(runCancellation.Token).ConfigureAwait(false);
        }
        finally
        {
            await runtime.DisposeAsync().ConfigureAwait(false);
        }
    }
}
