using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Outputs.Viiper;

internal sealed class CliViiperSettings
{
    public const string SectionName = "Viiper";

    public string? Host { get; set; }

    public int? Port { get; set; }

    public string? Password { get; set; }
}

internal static class ViiperConnection
{
    private const string DefaultHost = "127.0.0.1";
    private const int DefaultPort = 3242;
    private const string DefaultPassword = "";
    private const int DefaultSettleMs = 750;

    // MARK: Connection
    // ========================================================================

    internal static async Task<int> ExecuteMouseAsync(
        Func<ViiperMouseOutput, CancellationToken, Task<int>> action,
        CancellationToken cancellationToken,
        IServiceProvider? services = null)
    {
        return await ExecuteWithCancellationAsync(async ct =>
        {
            ViiperOptions viiperOptions = CreateViiperOptions(services);
            await ViiperServer.EnsureRunningAsync(viiperOptions, ct).ConfigureAwait(false);
            ViiperMouseOutput mouse = await ViiperMouseOutput
                .ConnectAsync(viiperOptions, ct)
                .ConfigureAwait(false);

            try
            {
                await Task.Delay(DefaultSettleMs, ct).ConfigureAwait(false);
                return await action(mouse, ct).ConfigureAwait(false);
            }
            finally
            {
                await mouse.DisposeAsync().ConfigureAwait(false);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<int> ExecuteXbox360Async(
        Func<ViiperXbox360Output, CancellationToken, Task<int>> action,
        CancellationToken cancellationToken,
        IServiceProvider? services = null)
    {
        return await ExecuteWithCancellationAsync(async ct =>
        {
            ViiperOptions viiperOptions = CreateViiperOptions(services);
            await ViiperServer.EnsureRunningAsync(viiperOptions, ct).ConfigureAwait(false);
            ViiperXbox360Output gamepad = await ViiperXbox360Output
                .ConnectAsync(viiperOptions, ct)
                .ConfigureAwait(false);

            try
            {
                await Task.Delay(DefaultSettleMs, ct).ConfigureAwait(false);
                return await action(gamepad, ct).ConfigureAwait(false);
            }
            finally
            {
                await gamepad.DisposeAsync().ConfigureAwait(false);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    internal static Task<int> ExecuteAsync(
        Func<ViiperMouseOutput, CancellationToken, Task<int>> action,
        CancellationToken cancellationToken,
        IServiceProvider? services = null)
    {
        return ExecuteMouseAsync(action, cancellationToken, services);
    }

    private static async Task<int> ExecuteWithCancellationAsync(
        Func<CancellationToken, Task<int>> action,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        void OnCancel(object? sender, ConsoleCancelEventArgs eventArgs)
        {
            eventArgs.Cancel = true;
            cancellationSource.Cancel();
        }

        Console.CancelKeyPress += OnCancel;

        try
        {
            return await action(cancellationSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
        {
            return 0;
        }
        finally
        {
            Console.CancelKeyPress -= OnCancel;
        }
    }

    internal static async Task PrintConnectionAsync(ViiperMouseOutput mouse)
    {
        string connectionStatus = mouse.IsConnected ? "yes" : "no";
        string busId = mouse.BusId?.ToString(CultureInfo.InvariantCulture) ?? "?";
        string deviceId = mouse.DeviceId ?? "?";
        await Console.Out.WriteLineAsync($"viiper connected={connectionStatus} bus={busId} device={deviceId}").ConfigureAwait(false);
    }

    internal static async Task PrintConnectionAsync(ViiperXbox360Output output)
    {
        string connectionStatus = output.IsConnected ? "yes" : "no";
        string busId = output.BusId?.ToString(CultureInfo.InvariantCulture) ?? "?";
        string deviceId = output.DeviceId ?? "?";
        await Console.Out.WriteLineAsync($"viiper xpad connected={connectionStatus} bus={busId} device={deviceId}").ConfigureAwait(false);
    }

    // MARK: Privates
    // ========================================================================

    internal static ViiperOptions CreateViiperOptions(
        IServiceProvider? services = null,
        ILogger? logger = null)
    {
        CliViiperSettings settings = services?
            .GetService<IOptions<CliViiperSettings>>()?
            .Value ?? new CliViiperSettings();

        int port = settings.Port switch
        {
            null => DefaultPort,
            < 1 or > 65_535 => throw new InvalidOperationException("Viiper:Port must be between 1 and 65535."),
            int value => value,
        };

        return new ViiperOptions
        {
            Host = string.IsNullOrWhiteSpace(settings.Host) ? DefaultHost : settings.Host,
            Port = port,
            Password = settings.Password ?? DefaultPassword,
            Logger = logger,
        };
    }
}
