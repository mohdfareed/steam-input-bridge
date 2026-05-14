using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

internal static class Program
{
    // MARK: Entry
    // ========================================================================

    private static async Task<int> Main()
    {
        string repoRoot = FindRepoRoot(AppContext.BaseDirectory);
        string project = Path.Combine(repoRoot, "tools", "SteamInput.TestBench", "SteamInput.TestBench.csproj");
        string output = Path.Combine(repoRoot, "artifacts", "steam-testbench");
        string executable = Path.Combine(output, "SteamInput.TestBench.exe");

        int publishExitCode = await RunProcessAsync(
            "dotnet",
            $"publish \"{project}\" --configuration Debug --runtime win-x64 --self-contained false --output \"{output}\"",
            repoRoot).ConfigureAwait(false);

        if (publishExitCode != 0)
        {
            await Console.Error.WriteLineAsync($"publish failed with exit code {publishExitCode}.").ConfigureAwait(false);
            await WaitForEnterAsync().ConfigureAwait(false);
            return publishExitCode;
        }

        using Process process = Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = output,
            UseShellExecute = false,
        }) ?? throw new InvalidOperationException("Could not start Steam Input testbench.");

        await process.WaitForExitAsync().ConfigureAwait(false);
        return process.ExitCode;
    }

    // MARK: Helpers
    // ========================================================================

    private static async Task<int> RunProcessAsync(string fileName, string arguments, string workingDirectory)
    {
        using Process process = Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
        }) ?? throw new InvalidOperationException($"Could not start {fileName}.");

        await process.WaitForExitAsync().ConfigureAwait(false);
        return process.ExitCode;
    }

    private static string FindRepoRoot(string startDirectory)
    {
        DirectoryInfo? directory = new(startDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "virtual-mouse.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find repository root.");
    }

    private static async Task WaitForEnterAsync()
    {
        await Console.Out.WriteLineAsync("Press Enter to exit.").ConfigureAwait(false);
        _ = await Console.In.ReadLineAsync().ConfigureAwait(false);
    }
}
