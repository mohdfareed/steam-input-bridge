using System;
using System.CommandLine;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PhysicalMouse.Viiper;

internal static class CliConnection
{
    // MARK: Connection
    // ========================================================================

    internal static ConnectionOptions AddOptions(Command command)
    {
        Option<string> hostOption = new("--host")
        {
            Description = "Host name or IP address.",
            DefaultValueFactory = _ => "127.0.0.1",
        };

        Option<int> portOption = new("--port")
        {
            Description = "TCP port.",
            DefaultValueFactory = _ => 3242,
        };

        Option<string> passwordOption = new("--password")
        {
            Description = "Server password.",
            DefaultValueFactory = _ => string.Empty,
        };

        Option<string?> logLevelOption = new("--log-level")
        {
            Description = "trace, debug, information, warning, error, critical, or none.",
        };

        Option<int> settleMsOption = new("--settle-ms")
        {
            Description = "Delay after connect before the first send.",
            DefaultValueFactory = _ => 750,
        };

        command.Options.Add(hostOption);
        command.Options.Add(portOption);
        command.Options.Add(passwordOption);
        command.Options.Add(logLevelOption);
        command.Options.Add(settleMsOption);

        logLevelOption.Validators.Add(result =>
        {
            string? value = result.GetValue(logLevelOption);
            if (!string.IsNullOrWhiteSpace(value) &&
                !string.Equals(value, "none", StringComparison.OrdinalIgnoreCase) &&
                !Enum.TryParse<LogLevel>(value, true, out _))
            {
                result.AddError("Invalid --log-level value.");
            }
        });

        return new ConnectionOptions(
            hostOption,
            portOption,
            passwordOption,
            logLevelOption,
            settleMsOption);
    }

    internal static async Task<int> ExecuteAsync(
        ParseResult parseResult,
        ConnectionOptions options,
        Func<ViiperPhysicalMouse, CancellationToken, Task<int>> action,
        CancellationToken cancellationToken)
    {
        using ILoggerFactory? loggerFactory = CreateLoggerFactory(parseResult.GetValue(options.LogLevelOption));
        ILogger? logger = loggerFactory?.CreateLogger("PhysicalMouse.Cli");
        ViiperOptions viiperOptions = CreateViiperOptions(parseResult, options, logger);
        ViiperPhysicalMouse mouse = await ViiperPhysicalMouse.ConnectAsync(viiperOptions, cancellationToken).ConfigureAwait(false);

        try
        {
            int settleMs = parseResult.GetValue(options.SettleMsOption);
            if (settleMs > 0)
            {
                await Task.Delay(settleMs, cancellationToken).ConfigureAwait(false);
            }

            return await action(mouse, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await mouse.DisposeAsync().ConfigureAwait(false);
        }
    }

    internal static async Task PrintConnectionAsync(ViiperPhysicalMouse mouse)
    {
        await Console.Out.WriteLineAsync($"Connected: {mouse.IsConnected}").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"BusId: {mouse.BusId?.ToString(CultureInfo.InvariantCulture) ?? "<unknown>"}").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"DeviceId: {mouse.DeviceId ?? "<unknown>"}").ConfigureAwait(false);
    }

    // MARK: Helpers
    // ========================================================================

    private static ViiperOptions CreateViiperOptions(ParseResult parseResult, ConnectionOptions options, ILogger? logger)
    {
        return new ViiperOptions
        {
            Host = parseResult.GetValue(options.HostOption) ?? "127.0.0.1",
            Port = parseResult.GetValue(options.PortOption),
            Password = parseResult.GetValue(options.PasswordOption) ?? string.Empty,
            Logger = logger,
        };
    }

    private static ILoggerFactory? CreateLoggerFactory(string? logLevel)
    {
        if (string.IsNullOrWhiteSpace(logLevel) ||
            string.Equals(logLevel, "none", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        LogLevel parsedLogLevel = Enum.Parse<LogLevel>(logLevel, ignoreCase: true);
        return LoggerFactory.Create(builder =>
        {
            _ = builder.SetMinimumLevel(parsedLogLevel);
            _ = builder.AddSimpleConsole(options =>
            {
                options.SingleLine = true;
                options.TimestampFormat = "HH:mm:ss.fff ";
            });
        });
    }

    internal readonly record struct ConnectionOptions(
        Option<string> HostOption,
        Option<int> PortOption,
        Option<string> PasswordOption,
        Option<string?> LogLevelOption,
        Option<int> SettleMsOption);
}
