using System;
using System.IO;
using Microsoft.Extensions.Logging;

namespace SteamInputBridge.Tests;

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
        string directory = Path.Combine(Path.GetTempPath(), "SteamInputBridge.Tests", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);
        string logDirectory = Path.Combine(directory, "logs");

        try
        {
            using ILoggerFactory factory = LoggerFactory.Create(logging =>
            {
                _ = logging.AddApplicationFileLogger(logDirectory);
            });

            ILogger logger = factory.CreateLogger("tests");
            LogSmokeTest(logger, null);

            string runLogPath = FileLoggingExtensions.ResolveRunLogFilePath(logDirectory);
            Assert.IsTrue(File.Exists(runLogPath));
            Assert.IsFalse(File.Exists(Path.Combine(logDirectory, "app.log")));

            string text = File.ReadAllText(runLogPath);
            StringAssert.Contains(text, "file logger smoke test", StringComparison.Ordinal);
            StringAssert.Contains(text, "tests", StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
