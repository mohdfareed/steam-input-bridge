using System;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Hosting;

/// <summary>Serves local forwarding control commands.</summary>
internal sealed class ForwardingHostServer(
    ForwardingHost host,
    string pipeName,
    ILogger? logger = null)
{
    private static readonly Encoding PipeEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Default local pipe name.</summary>
    public const string DefaultPipeName = "Hosting";

    /// <summary>Creates a server using the default pipe name.</summary>
    /// <param name="host">Forwarding host.</param>
    public ForwardingHostServer(ForwardingHost host)
        : this(host, DefaultPipeName, logger: null)
    {
    }

    /// <summary>Creates a server using the default pipe name.</summary>
    /// <param name="host">Forwarding host.</param>
    /// <param name="logger">Logger for lifecycle events.</param>
    public ForwardingHostServer(ForwardingHost host, ILogger? logger)
        : this(host, DefaultPipeName, logger)
    {
    }

    /// <summary>Runs the control server until cancelled.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        ForwardingHostControlLog.StartingServer(logger, pipeName);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
#pragma warning disable CA2000 // Ownership transfers to the client handling task.
            NamedPipeServerStream pipe = CreatePipe();
#pragma warning restore CA2000
            using CancellationTokenRegistration registration = cancellationToken.Register(static target =>
            {
                ((NamedPipeServerStream)target!).Dispose();
            }, pipe);

            try
            {
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            NamedPipeServerStream connectedPipe = pipe;
            pipe = null!;
            _ = Task.Run(async () =>
            {
                try
                {
                    using (connectedPipe)
                    {
                        await HandleConnectionAsync(connectedPipe, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                }
                catch (IOException exception)
                {
                    ForwardingHostControlLog.ConnectionClosed(logger, exception);
                }
            }, CancellationToken.None);
        }
    }

    private NamedPipeServerStream CreatePipe()
    {
        return new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 254,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
    }

    private async Task HandleConnectionAsync(Stream stream, CancellationToken cancellationToken)
    {
        using StreamReader reader = new(stream, PipeEncoding, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        using StreamWriter writer = new(stream, PipeEncoding, leaveOpen: true)
        {
            NewLine = "\n",
            AutoFlush = true,
        };

        string? command = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        ForwardingHostControlLog.ReceivedCommand(logger, command ?? "(empty)");

        if (IsEnableCommand(command))
        {
            await HoldEnableLeaseAsync(stream, writer, cancellationToken).ConfigureAwait(false);
            return;
        }

        string response = Execute(command);
        await writer.WriteLineAsync(response.AsMemory(), cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private string Execute(string? command)
    {
        return ForwardingHostControlProtocol.Execute(host, command);
    }

    private async Task HoldEnableLeaseAsync(
        Stream stream,
        StreamWriter writer,
        CancellationToken cancellationToken)
    {
        using IDisposable lease = host.Enable();
        await writer.WriteLineAsync("OK enabled".AsMemory(), cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        ForwardingHostControlLog.LeaseOpened(logger, host.RouteId);

        byte[] buffer = new byte[1];
        while (await stream.ReadAsync(buffer.AsMemory(0, 1), cancellationToken).ConfigureAwait(false) > 0)
        {
        }

        ForwardingHostControlLog.LeaseClosed(logger, host.RouteId);
    }

    private static bool IsEnableCommand(string? command)
    {
        return string.Equals(command?.Trim(), "ENABLE", StringComparison.OrdinalIgnoreCase);
    }
}

internal static class ForwardingHostControlProtocol
{
    public static string Execute(ForwardingHost host, string? command)
    {
        return command?.Trim().ToUpperInvariant() switch
        {
            "STATUS" => FormatStatus(host),
            null or "" => "ERR empty command",
            _ => "ERR unknown command",
        };
    }

    public static ForwardingStatus ParseStatus(string response)
    {
        const string Prefix = "STATUS route=";
        const string EnabledSeparator = " enabled=";
        const string ConnectedSeparator = " connected=";
        const string CountSeparator = " enabledClients=";

        if (!response.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new FormatException("Host returned an invalid status response.");
        }

        int enabledIndex = response.IndexOf(EnabledSeparator, StringComparison.Ordinal);
        int connectedIndex = response.IndexOf(ConnectedSeparator, StringComparison.Ordinal);
        int countIndex = response.IndexOf(CountSeparator, StringComparison.Ordinal);
        ThrowIfInvalidSeparator(enabledIndex);
        ThrowIfInvalidSeparator(connectedIndex);
        ThrowIfInvalidSeparator(countIndex);

        return new ForwardingStatus(
            response[Prefix.Length..enabledIndex],
            bool.Parse(response[(enabledIndex + EnabledSeparator.Length)..connectedIndex]),
            bool.Parse(response[(connectedIndex + ConnectedSeparator.Length)..countIndex]),
            int.Parse(response[(countIndex + CountSeparator.Length)..], CultureInfo.InvariantCulture));
    }

    private static void ThrowIfInvalidSeparator(int index)
    {
        if (index >= 0)
        {
            return;
        }

        throw new FormatException("Host returned an invalid status response.");
    }

    private static string FormatStatus(ForwardingHost host)
    {
        string enabled = host.IsEnabled ? "true" : "false";
        string connected = host.IsConnected ? "true" : "false";
        return $"STATUS route={host.RouteId} enabled={enabled} connected={connected} enabledClients={host.EnabledLeaseCount}";
    }
}

internal static class ForwardingHostControlLog
{
    private static readonly Action<ILogger, string, Exception?> StartingServerMessage =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(1, nameof(StartingServer)),
            "Starting host control server on pipe {PipeName}.");

    private static readonly Action<ILogger, Exception?> ConnectionClosedMessage =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(2, nameof(ConnectionClosed)),
            "Host control connection closed.");

    private static readonly Action<ILogger, string, Exception?> ReceivedCommandMessage =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(3, nameof(ReceivedCommand)),
            "Received host control command {Command}.");

    private static readonly Action<ILogger, string, Exception?> LeaseOpenedMessage =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(4, nameof(LeaseOpened)),
            "Host enable lease opened for route {RouteId}.");

    private static readonly Action<ILogger, string, Exception?> LeaseClosedMessage =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(5, nameof(LeaseClosed)),
            "Host enable lease closed for route {RouteId}.");

    public static void StartingServer(ILogger? logger, string pipeName)
    {
        if (logger is not null)
        {
            StartingServerMessage(logger, pipeName, null);
        }
    }

    public static void ConnectionClosed(ILogger? logger, Exception exception)
    {
        if (logger is not null)
        {
            ConnectionClosedMessage(logger, exception);
        }
    }

    public static void ReceivedCommand(ILogger? logger, string command)
    {
        if (logger is not null)
        {
            ReceivedCommandMessage(logger, command, null);
        }
    }

    public static void LeaseOpened(ILogger? logger, string routeId)
    {
        if (logger is not null)
        {
            LeaseOpenedMessage(logger, routeId, null);
        }
    }

    public static void LeaseClosed(ILogger? logger, string routeId)
    {
        if (logger is not null)
        {
            LeaseClosedMessage(logger, routeId, null);
        }
    }
}
