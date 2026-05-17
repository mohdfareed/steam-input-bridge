using System;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Inputs.RawInput;
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

/// <summary>Local forwarding server options.</summary>
public sealed record ForwardingServerOptions
{
    /// <summary>Route to host.</summary>
    public ForwardingRouteKind Route { get; init; }

    /// <summary>SDL gamepad options for xpad routes.</summary>
    public SdlGamepadOptions SdlGamepad { get; init; } = new();

    /// <summary>VIIPER connection options.</summary>
    public required ViiperOptions Viiper { get; init; }

    /// <summary>Lifecycle logger.</summary>
    public ILogger? Logger { get; init; }
}

internal readonly record struct ForwardingRouteRuntime(
    string RouteId,
    string PipeName,
    string OwnershipName);

/// <summary>Runs a local forwarding server.</summary>
public sealed class ForwardingServer(ForwardingServerOptions options) : IHostedService, IAsyncDisposable
{
    private static readonly ForwardingRouteRuntime MouseRoute = new(
        ForwardingRouteIds.Mouse,
        "Hosting",
        @"Local\Hosting");
    private static readonly ForwardingRouteRuntime XpadRoute = new(
        ForwardingRouteIds.Xpad,
        "Hosting.xpad",
        @"Local\Hosting.xpad");
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
        return GetRouteRuntime(route).RouteId;
    }

    /// <summary>Gets the control pipe name for a route.</summary>
    public static string GetPipeName(ForwardingRouteKind route)
    {
        return GetRouteRuntime(route).PipeName;
    }

    /// <summary>Gets the single-instance ownership name for a route.</summary>
    public static string GetOwnershipName(ForwardingRouteKind route)
    {
        return GetRouteRuntime(route).OwnershipName;
    }

    private static ForwardingRouteRuntime GetRouteRuntime(ForwardingRouteKind route)
    {
        return route switch
        {
            ForwardingRouteKind.Mouse => MouseRoute,
            ForwardingRouteKind.Xpad => XpadRoute,
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

        using HostSingleInstance instance = HostSingleInstance.TryAcquire(GetOwnershipName(options.Route)) ??
            throw new InvalidOperationException("Another forwarding host is already running for this route.");
        IForwardingRoute route = await CreateRouteAsync(options, cancellationToken).ConfigureAwait(false);
        ForwardingHost host = new(route, options.Logger);
        ForwardingHostServer server = new(
            host,
            GetPipeName(options.Route),
            options.Logger);

        using CancellationTokenSource runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await using (host.ConfigureAwait(false))
        {
            Task forwardingTask = Task.Run(() => host.Run(runCancellation.Token), CancellationToken.None);
            Task controlTask = server.RunAsync(runCancellation.Token);

            await WaitForStopAsync(forwardingTask, controlTask, runCancellation).ConfigureAwait(false);
        }
    }

    private static Task<IForwardingRoute> CreateRouteAsync(
        ForwardingServerOptions options,
        CancellationToken cancellationToken)
    {
#pragma warning disable CA2000 // Ownership transfers to ForwardingHost.
        return options.Route switch
        {
            ForwardingRouteKind.Mouse => CreateMouseRouteAsync(options.Viiper, cancellationToken),
            ForwardingRouteKind.Xpad => CreateXpadRouteAsync(
                options.Viiper,
                options.SdlGamepad,
                cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };
#pragma warning restore CA2000
    }

    private static Task<IForwardingRoute> CreateMouseRouteAsync(
        ViiperOptions viiperOptions,
        CancellationToken cancellationToken)
    {
        return OperatingSystem.IsWindows()
            ? CreateWindowsMouseRouteAsync(viiperOptions, cancellationToken)
            : throw new PlatformNotSupportedException("Mouse host routes require Windows.");
    }

    [SupportedOSPlatform("windows")]
    private static async Task<IForwardingRoute> CreateWindowsMouseRouteAsync(
        ViiperOptions viiperOptions,
        CancellationToken cancellationToken)
    {
        RawInputMouseSource? input = null;
        ViiperMouseOutput? output = null;

        try
        {
            input = await RawInputMouseSource
                .ConnectAsync(cancellationToken)
                .ConfigureAwait(false);
            await ViiperServer.EnsureRunningAsync(viiperOptions, cancellationToken).ConfigureAwait(false);
            output = await ViiperMouseOutput
                .ConnectAsync(viiperOptions, cancellationToken)
                .ConfigureAwait(false);

            MouseForwardingRoute route = new(input, output);
            input = null;
            output = null;
            return route;
        }
        finally
        {
            if (input is not null)
            {
                await input.DisposeAsync().ConfigureAwait(false);
            }

            if (output is not null)
            {
                await output.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static async Task<IForwardingRoute> CreateXpadRouteAsync(
        ViiperOptions viiperOptions,
        SdlGamepadOptions sdlOptions,
        CancellationToken cancellationToken)
    {
        SdlGamepadSource? input = null;
        ViiperXbox360Output? output = null;

        try
        {
            input = await SdlGamepadSource
                .ConnectAsync(sdlOptions, cancellationToken)
                .ConfigureAwait(false);
            await ViiperServer.EnsureRunningAsync(viiperOptions, cancellationToken).ConfigureAwait(false);
            output = await ViiperXbox360Output
                .ConnectAsync(viiperOptions, cancellationToken)
                .ConfigureAwait(false);

            Xbox360ForwardingRoute route = new(input, output);
            input = null;
            output = null;
            return route;
        }
        finally
        {
            if (input is not null)
            {
                await input.DisposeAsync().ConfigureAwait(false);
            }

            if (output is not null)
            {
                await output.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static async Task WaitForStopAsync(
        Task forwardingTask,
        Task controlTask,
        CancellationTokenSource cancellationSource)
    {
        _ = await Task.WhenAny(forwardingTask, controlTask).ConfigureAwait(false);
        await cancellationSource.CancelAsync().ConfigureAwait(false);

        try
        {
            await Task.WhenAll(forwardingTask, controlTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
        {
        }
    }
}
