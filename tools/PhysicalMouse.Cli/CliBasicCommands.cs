using System;
using System.CommandLine;
using PhysicalMouse;

internal static class CliBasicCommands
{
    // MARK: Commands
    // ========================================================================

    internal static Command CreateConnectCommand()
    {
        Command command = new("connect", "Connect and print the active IDs.");
        CliConnection.ConnectionOptions options = CliConnection.AddOptions(command);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            _ = await CliConnection.ExecuteAsync(
                parseResult,
                options,
                async (mouse, _) =>
                {
                    await CliConnection.PrintConnectionAsync(mouse).ConfigureAwait(false);
                    return 0;
                },
                cancellationToken).ConfigureAwait(false);
        });

        return command;
    }

    internal static Command CreateMoveCommand()
    {
        Command command = new("move", "Send one relative move report.");
        Option<int> dxOption = new("--dx")
        {
            Description = "Horizontal delta.",
        };

        Option<int> dyOption = new("--dy")
        {
            Description = "Vertical delta.",
        };

        command.Options.Add(dxOption);
        command.Options.Add(dyOption);
        CliConnection.ConnectionOptions options = CliConnection.AddOptions(command);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            int dx = parseResult.GetValue(dxOption);
            int dy = parseResult.GetValue(dyOption);

            _ = await CliConnection.ExecuteAsync(
                parseResult,
                options,
                async (mouse, ct) =>
                {
                    await mouse.SendAsync(new MouseReport(MouseButtons.None, dx, dy, 0), ct).ConfigureAwait(false);
                    await CliConnection.PrintConnectionAsync(mouse).ConfigureAwait(false);
                    return 0;
                },
                cancellationToken).ConfigureAwait(false);
        });

        return command;
    }

    internal static Command CreateClickCommand()
    {
        Command command = new("click", "Send a button press and release.");
        Option<string> buttonOption = new("--button")
        {
            Description = "left, right, middle, back, or forward.",
            Required = true,
        };

        command.Options.Add(buttonOption);
        CliConnection.ConnectionOptions options = CliConnection.AddOptions(command);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            MouseButtons button = ParseButton(parseResult.GetValue(buttonOption) ?? throw new InvalidOperationException("Missing required --button value."));

            _ = await CliConnection.ExecuteAsync(
                parseResult,
                options,
                async (mouse, ct) =>
                {
                    await mouse.SendAsync(new MouseReport(button, 0, 0, 0), ct).ConfigureAwait(false);
                    await mouse.SendAsync(MouseReport.Empty, ct).ConfigureAwait(false);
                    await CliConnection.PrintConnectionAsync(mouse).ConfigureAwait(false);
                    return 0;
                },
                cancellationToken).ConfigureAwait(false);
        });

        return command;
    }

    internal static Command CreateWheelCommand()
    {
        Command command = new("wheel", "Send one vertical wheel report.");
        Option<int> deltaOption = new("--delta")
        {
            Description = "Wheel delta.",
            Required = true,
        };

        command.Options.Add(deltaOption);
        CliConnection.ConnectionOptions options = CliConnection.AddOptions(command);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            int delta = parseResult.GetValue(deltaOption);

            _ = await CliConnection.ExecuteAsync(
                parseResult,
                options,
                async (mouse, ct) =>
                {
                    await mouse.SendAsync(new MouseReport(MouseButtons.None, 0, 0, delta), ct).ConfigureAwait(false);
                    await CliConnection.PrintConnectionAsync(mouse).ConfigureAwait(false);
                    return 0;
                },
                cancellationToken).ConfigureAwait(false);
        });

        return command;
    }

    // MARK: Helpers
    // ========================================================================

    private static MouseButtons ParseButton(string value)
    {
        return string.Equals(value, "left", StringComparison.OrdinalIgnoreCase) ? MouseButtons.Left :
            string.Equals(value, "right", StringComparison.OrdinalIgnoreCase) ? MouseButtons.Right :
            string.Equals(value, "middle", StringComparison.OrdinalIgnoreCase) ? MouseButtons.Middle :
            string.Equals(value, "back", StringComparison.OrdinalIgnoreCase) ? MouseButtons.Back :
            string.Equals(value, "forward", StringComparison.OrdinalIgnoreCase) ? MouseButtons.Forward :
            throw new ArgumentException($"Unknown button '{value}'.");
    }
}
