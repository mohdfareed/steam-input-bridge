using System;
using System.Globalization;
using System.IO;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace VirtualMouse.Settings;

// MARK: Dependency Injection
// ============================================================================

/// <summary>File logging registration for application composition.</summary>
public static class FileLoggingExtensions
{
    /// <summary>Adds file logging when a log file path is configured.</summary>
    public static ILoggingBuilder AddApplicationFileLogger(
        this ILoggingBuilder logging,
        string? logFile)
    {
        ArgumentNullException.ThrowIfNull(logging);

        if (string.IsNullOrWhiteSpace(logFile))
        {
            return logging;
        }

        _ = logging.Services.AddSingleton<ILoggerProvider>(_ => new FileLoggerProvider(logFile));
        return logging;
    }
}

// MARK: Implementation
// ============================================================================

internal sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly Lock _gate = new();
    private readonly string _path;
    private bool _disposed;

    public FileLoggerProvider(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _path = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            _ = Directory.CreateDirectory(directory);
        }
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new FileLogger(categoryName, Write);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
        }
    }

    private void Write(string categoryName, LogLevel logLevel, string message, Exception? exception)
    {
        string timestamp = DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture);
        string line = $"{timestamp} [{logLevel}] {categoryName}: {message}";
        if (exception is not null)
        {
            line += Environment.NewLine + exception;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            File.AppendAllText(_path, line + Environment.NewLine);
        }
    }
}

internal sealed class FileLogger(
    string categoryName,
    Action<string, LogLevel, string, Exception?> write) : ILogger
{
    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull
    {
        return EmptyScope.Instance;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return logLevel != LogLevel.None;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);

        if (!IsEnabled(logLevel))
        {
            return;
        }

        string message = formatter(state, exception);
        if (message.Length == 0 && exception is null)
        {
            return;
        }

        write(categoryName, logLevel, message, exception);
    }
}

internal sealed class EmptyScope : IDisposable
{
    public static readonly EmptyScope Instance = new();

    public void Dispose()
    {
    }
}
