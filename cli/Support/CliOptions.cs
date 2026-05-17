using System.CommandLine;
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

    internal static Option<int?> CreateWaitMsOption(int defaultWaitMs)
    {
        return CreateNonNegativeIntOption(
            "--wait-ms",
            $"Wait for matching devices before failing. Default: {defaultWaitMs}.");
    }

    internal static Option<bool> CreatePauseOption()
    {
        return new Option<bool>("--pause")
        {
            Description = "Wait for Enter before exiting.",
        };
    }

    internal static Option<string?> CreateGamepadOption(string name, string description)
    {
        return new Option<string?>(name)
        {
            Description = description,
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
