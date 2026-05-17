using System;
using System.CommandLine;
using Hosting;
using Inputs.Sdl;

internal static class CliOptions
{
    internal static Option<int?> CreateCountOption(int defaultCount)
    {
        return CreatePositiveIntOption(
            "--count",
            $"Measured reports. Default: {defaultCount}.");
    }

    internal static Option<int?> CreateDurationMsOption(int defaultDurationMs)
    {
        return CreatePositiveIntOption(
            "--duration-ms",
            $"Button press duration. Default: {defaultDurationMs}.");
    }

    internal static Option<int?> CreateDeviceIndexOption(string name, string description)
    {
        return CreateNonNegativeIntOption(name, description);
    }

    internal static Option<int?> CreatePollMsOption(string name, string description)
    {
        return CreateNonNegativeIntOption(name, description);
    }

    internal static Option<ForwardingRouteKind?> CreateRouteOption()
    {
        return new Option<ForwardingRouteKind?>("--route")
        {
            Description = "Route to control. Default: mouse.",
        };
    }

    internal static Option<ForwardingRouteKind> CreateRequiredRouteOption()
    {
        return new Option<ForwardingRouteKind>("--route")
        {
            Description = "Route to control.",
            Required = true,
        };
    }

    internal static Option<string?> CreateSteamPathOption()
    {
        return new Option<string?>("--steam-path")
        {
            Description = "Steam install path. Defaults to SteamPath/SteamDir, registry, or common install paths.",
        };
    }

    internal static Option<uint?> CreateUserIdOption()
    {
        return new Option<uint?>("--user-id")
        {
            Description = "Steam userdata id for non-Steam shortcuts. Defaults to Steam's active user when available.",
        };
    }

    internal static Argument<uint> CreateAppIdArgument()
    {
        Argument<uint> argument = new("app-id")
        {
            Description = "Steam app id or non-Steam shortcut app id.",
        };
        argument.Validators.Add(result =>
        {
            if (result.GetValue(argument) == 0)
            {
                result.AddError("app-id must be greater than 0.");
            }
        });
        return argument;
    }

    internal static SdlGamepadOptions CreateSdlGamepadOptions(
        ParseResult parseResult,
        Option<int?> deviceIndexOption,
        Option<int?> pollMsOption)
    {
        return new SdlGamepadOptions
        {
            DeviceIndex = parseResult.GetValue(deviceIndexOption) ?? 0,
            PollInterval = TimeSpan.FromMilliseconds(parseResult.GetValue(pollMsOption) ?? 1),
        };
    }

    private static Option<int?> CreatePositiveIntOption(string name, string description)
    {
        Option<int?> option = new(name)
        {
            Description = description,
        };
        option.Validators.Add(result =>
        {
            int? value = result.GetValue(option);
            if (value.HasValue && value.Value <= 0)
            {
                result.AddError($"{name} must be greater than 0.");
            }
        });
        return option;
    }

    private static Option<int?> CreateNonNegativeIntOption(string name, string description)
    {
        Option<int?> option = new(name)
        {
            Description = description,
        };
        option.Validators.Add(result =>
        {
            int? value = result.GetValue(option);
            if (value.HasValue && value.Value < 0)
            {
                result.AddError($"{name} must be greater than or equal to 0.");
            }
        });
        return option;
    }
}
