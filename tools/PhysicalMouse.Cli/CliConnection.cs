using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using PhysicalMouse.Viiper;

internal static class CliConnection
{
    private const string DefaultHost = "127.0.0.1";
    private const int DefaultPort = 3242;
    private const string DefaultPassword = "";
    private const int DefaultSettleMs = 750;

    // MARK: Connection
    // ========================================================================

    internal static async Task<int> ExecuteAsync(
        Func<ViiperPhysicalMouse, CancellationToken, Task<int>> action,
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
            ViiperOptions viiperOptions = CreateViiperOptions();
            ViiperPhysicalMouse mouse = await ViiperPhysicalMouse.ConnectAsync(viiperOptions, cancellationSource.Token).ConfigureAwait(false);

            try
            {
                await Task.Delay(DefaultSettleMs, cancellationSource.Token).ConfigureAwait(false);
                return await action(mouse, cancellationSource.Token).ConfigureAwait(false);
            }
            finally
            {
                await mouse.DisposeAsync().ConfigureAwait(false);
            }
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

    internal static async Task PrintConnectionAsync(ViiperPhysicalMouse mouse)
    {
        await Console.Out.WriteLineAsync($"Connected: {mouse.IsConnected}").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"BusId: {mouse.BusId?.ToString(CultureInfo.InvariantCulture) ?? "<unknown>"}").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"DeviceId: {mouse.DeviceId ?? "<unknown>"}").ConfigureAwait(false);
    }

    // MARK: Helpers
    // ========================================================================

    private static ViiperOptions CreateViiperOptions()
    {
        return new ViiperOptions
        {
            Host = DefaultHost,
            Port = DefaultPort,
            Password = DefaultPassword,
        };
    }
}
