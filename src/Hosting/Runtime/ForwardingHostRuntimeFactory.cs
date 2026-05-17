using System;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Inputs.RawInput;
using Outputs.Viiper;

namespace Hosting;

internal static class ForwardingHostRuntimeFactory
{
    public static ForwardingHostRuntime Create(ForwardingServerOptions options)
    {
#pragma warning disable CA2000
        ForwardingHostState hostState = new();
        HostedRouteController mouse = new(
            ForwardingRouteIds.Mouse,
            ct => CreateMouseRouteAsync(options.Viiper, ct),
            options.Logger,
            () => hostState.EmulationEnabled);
        GamepadControllerRegistry gamepads = new(options.Viiper, hostState, options.Logger);

        return new ForwardingHostRuntime(
            mouse,
            gamepads,
            hostState);
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
            input = await RawInputMouseSource.ConnectAsync(cancellationToken).ConfigureAwait(false);
            await ViiperServer.EnsureRunningAsync(viiperOptions, cancellationToken).ConfigureAwait(false);
            output = await ViiperMouseOutput.ConnectAsync(viiperOptions, cancellationToken).ConfigureAwait(false);

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

}
