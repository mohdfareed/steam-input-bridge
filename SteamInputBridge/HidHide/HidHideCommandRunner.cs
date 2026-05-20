using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace SteamInputBridge.HidHide;

/// <summary>Runs HidHide commands.</summary>
public interface IHidHideCommandRunner
{
    /// <summary>Runs a HidHide command and returns stdout.</summary>
    string Run(IReadOnlyList<string> args);
}

/// <summary>HidHideCLI.exe command runner.</summary>
public sealed class HidHideCliRunner(string cliPath) : IHidHideCommandRunner
{
    /// <inheritdoc />
    public string Run(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (!File.Exists(cliPath))
        {
            throw new FileNotFoundException("HidHideCLI.exe was not found.", cliPath);
        }

        ProcessStartInfo start = new()
        {
            FileName = cliPath,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        using Process process = Process.Start(start) ??
            throw new InvalidOperationException($"Could not start {cliPath}.");
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return process.ExitCode == 0
            ? output
            : throw new InvalidOperationException(
                $"{Path.GetFileName(cliPath)} failed with exit code {process.ExitCode}: {error}{output}");
    }
}
