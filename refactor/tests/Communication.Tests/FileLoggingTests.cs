using System;
using System.IO;
using Microsoft.Extensions.Logging;
using VirtualMouse.Settings;

namespace Communication.Tests;

/// <summary>Tests application file logging registration.</summary>
[TestClass]
public sealed class FileLoggingTests
{
    private static readonly Action<ILogger, Exception?> LogSmokeTest =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(1, nameof(LogSmokeTest)),
            "file logger smoke test");

    /// <summary>Checks that configured file logging writes log lines.</summary>
    [TestMethod]
    public void FileLoggerWritesConfiguredFile()
    {
        string directory = Path.Combine(Path.GetTempPath(), "VirtualMouse.Refactor.Tests", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);
        string logPath = Path.Combine(directory, "app.log");

        try
        {
            using ILoggerFactory factory = LoggerFactory.Create(logging =>
            {
                _ = logging.AddApplicationFileLogger(logPath);
            });

            ILogger logger = factory.CreateLogger("tests");
            LogSmokeTest(logger, null);

            string text = File.ReadAllText(logPath);
            StringAssert.Contains(text, "file logger smoke test", StringComparison.Ordinal);
            StringAssert.Contains(text, "tests", StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
