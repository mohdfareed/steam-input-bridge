using System;
using System.Threading;
using System.Threading.Tasks;
using Inputs.Sdl;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Outputs.Viiper;

namespace Hosting;

/// <summary>Local host route kind.</summary>
public enum ForwardingRouteKind
{
    /// <summary>Raw Input mouse to VIIPER mouse.</summary>
    Mouse,

    /// <summary>SDL gamepad to VIIPER Xbox 360.</summary>
    Xpad,
}

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
/// <param name="Xpad">Gamepad route status.</param>
/// <param name="XpadDeviceIndex">Configured SDL gamepad index.</param>
/// <param name="XpadDeviceName">Configured SDL gamepad name when known.</param>
public readonly record struct ForwardingHostStatus(
    ForwardingRouteStatus Mouse,
    ForwardingRouteStatus Xpad,
    int XpadDeviceIndex,
    string? XpadDeviceName);

/// <summary>Local forwarding server options.</summary>
public sealed record ForwardingServerOptions
{
    /// <summary>SDL gamepad options for xpad routes.</summary>
    public SdlGamepadOptions SdlGamepad { get; init; } = new();

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

    /// <summary>Gets the route id for a route kind.</summary>
    public static string GetRouteId(ForwardingRouteKind route)
    {
        return route switch
        {
            ForwardingRouteKind.Mouse => ForwardingRouteIds.Mouse,
            ForwardingRouteKind.Xpad => ForwardingRouteIds.Xpad,
            _ => throw new ArgumentOutOfRangeException(nameof(route)),
        };
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
        ArgumentNullException.ThrowIfNull(options.SdlGamepad);

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
