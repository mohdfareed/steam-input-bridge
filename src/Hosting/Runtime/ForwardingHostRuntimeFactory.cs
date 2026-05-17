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
        SdlDeviceSelection xpadDeviceSelection = ResolveSdlOptions(options.SdlGamepad);
        ForwardingHostState hostState = new();
        HostedRouteController mouse = new(
            ForwardingRouteIds.Mouse,
            ct => CreateMouseRouteAsync(options.Viiper, ct),
            options.Logger,
            () => hostState.EmulationEnabled);
        HostedRouteController xpad = new(
            ForwardingRouteIds.Xpad,
            ct => CreateXpadRouteAsync(options.Viiper, xpadDeviceSelection.Options, hostState, ct),
            options.Logger,
            () => hostState.EmulationEnabled);

        return new ForwardingHostRuntime(
            mouse,
            xpad,
            options.SdlGamepad.DeviceIndex,
            xpadDeviceSelection.UsesPhysicalMotion,
            hostState,
            xpadDeviceSelection.DeviceName,
            xpadDeviceSelection.MotionDeviceIndex,
            xpadDeviceSelection.MotionDeviceName);
#pragma warning restore CA2000
    }

    private static SdlDeviceSelection ResolveSdlOptions(SdlGamepadOptions options)
    {
        IReadOnlyList<SdlGamepadInfo> gamepads = SdlGamepadSource.GetGamepads();
        SdlGamepadOptions resolvedOptions = SdlGamepadMotionSelector.ResolveOptions(gamepads, options);
        SdlGamepadInfo gamepad = SdlGamepadMotionSelector.GetGamepad(gamepads, resolvedOptions.DeviceIndex);
        SdlMotionDeviceSelection motion = SdlGamepadSource.ResolveMotionDevice(gamepads, gamepad, resolvedOptions);
        return new SdlDeviceSelection(
            resolvedOptions,
            gamepad.Name,
            motion.UsesSecondaryDevice,
            motion.UsesSecondaryDevice ? motion.DeviceIndex : null,
            motion.UsesSecondaryDevice ? motion.Device?.Name : null);
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
        ForwardingHostState hostState,
        CancellationToken cancellationToken)
    {
        SdlGamepadSource? input = null;
        ViiperXbox360Output? output = null;

        try
        {
            input = await SdlGamepadSource.ConnectAsync(sdlOptions, cancellationToken).ConfigureAwait(false);
            await ViiperServer.EnsureRunningAsync(viiperOptions, cancellationToken).ConfigureAwait(false);
            output = await ViiperXbox360Output.ConnectAsync(viiperOptions, cancellationToken).ConfigureAwait(false);

            Xbox360ForwardingRoute route = new(
                input,
                output,
                shouldForwardMotion: () => hostState.PhysicalMotionEnabled);
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

    private readonly record struct SdlDeviceSelection(
        SdlGamepadOptions Options,
        string? DeviceName,
        bool UsesPhysicalMotion,
        int? MotionDeviceIndex,
        string? MotionDeviceName);
}
