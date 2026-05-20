using System;

namespace SteamInputBridge.HidHide;

/// <summary>Ensures this process can see HidHide-hidden devices.</summary>
public sealed class HidHideApplicationAccess(IHidHideCommandRunner runner)
{
    /// <summary>Registers the current executable with HidHide's allowed application list.</summary>
    public void AllowCurrentProcess()
    {
        if (Environment.ProcessPath is { Length: > 0 } processPath)
        {
            AllowApplication(processPath);
        }
    }

    /// <summary>Registers an executable with HidHide's allowed application list.</summary>
    public void AllowApplication(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string applications = runner.Run(["--app-list"]);
        if (ContainsLineValue(applications, path))
        {
            return;
        }

        _ = runner.Run(["--app-reg", path]);
    }

    private static bool ContainsLineValue(string output, string value)
    {
        foreach (string line in output.Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.Contains(value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
