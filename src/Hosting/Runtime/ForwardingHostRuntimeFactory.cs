using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Inputs.RawInput;
using Inputs.Sdl;
using Outputs.Viiper;

namespace Hosting;

internal static class ForwardingHostRuntimeFactory
{
    public static ForwardingHostRuntime Create(ForwardingServerOptions options)
    {
#pragma warning disable CA2000
        string? xpadDeviceName = ValidateSdlOptions(options.SdlGamepad);
        HostedRouteController mouse = new(
            ForwardingRouteIds.Mouse,
            ct => CreateMouseRouteAsync(options.Viiper, ct),
            options.Logger);
        HostedRouteController xpad = new(
            ForwardingRouteIds.Xpad,
            ct => CreateXpadRouteAsync(options.Viiper, options.SdlGamepad, ct),
            options.Logger);

        return new ForwardingHostRuntime(
            mouse,
            xpad,
            options.SdlGamepad.DeviceIndex,
            xpadDeviceName);
#pragma warning restore CA2000
    }

    private static string? ValidateSdlOptions(SdlGamepadOptions options)
    {
        IReadOnlyList<SdlGamepadInfo> gamepads = SdlGamepadSource.GetGamepads();
        int deviceIndex = options.DeviceIndex;
        return deviceIndex < 0 || deviceIndex >= gamepads.Count
            ? throw new InvalidOperationException($"SDL gamepad index {deviceIndex} is not available.")
            : gamepads[deviceIndex].Name;
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

    private static async Task<IForwardingRoute> CreateXpadRouteAsync(
        ViiperOptions viiperOptions,
        SdlGamepadOptions sdlOptions,
        CancellationToken cancellationToken)
    {
        SdlGamepadSource? input = null;
        ViiperXbox360Output? output = null;

        try
        {
            input = await SdlGamepadSource.ConnectAsync(sdlOptions, cancellationToken).ConfigureAwait(false);
            await ViiperServer.EnsureRunningAsync(viiperOptions, cancellationToken).ConfigureAwait(false);
            output = await ViiperXbox360Output.ConnectAsync(viiperOptions, cancellationToken).ConfigureAwait(false);

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
}
