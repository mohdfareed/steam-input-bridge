using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SteamInputBridge.App.Host;
using SteamInputBridge.Outputs.Teensy;
using SteamInputBridge.Settings;

namespace SteamInputBridge.App;

internal sealed class TeensyFirmwareUploader(
    AppEnvironment environment,
    SettingsFile settingsFile,
    SettingsService settings,
    TeensyMouseOutputService teensy,
    CancellationToken cancellationToken)
{
    private const string TeensyBoard = "TEENSY40";

    // MARK: Publics
    // ========================================================================

    public async Task UploadAsync()
    {
        string firmwareDirectory = ResolveFirmwareDirectory(settings.Current.Teensy.FirmwareDirectory);
        string firmwarePath = ResolveFirmwarePath(firmwareDirectory);
        TeensyUploadTool tool = FindUploadTool(firmwareDirectory) ??
            throw new FileNotFoundException(
                "Teensy uploader was not found. Put teensy_post_compile.exe or teensy_loader_cli.exe in the firmware directory, or install PlatformIO's Teensy tools.",
                Path.Combine(firmwareDirectory, "teensy_post_compile.exe"));

        using IDisposable pause = teensy.PauseConnection();
        await RunUploadToolAsync(tool, firmwarePath, cancellationToken).ConfigureAwait(false);
    }

    // MARK: Implementation
    // ========================================================================

    private static async Task RunUploadToolAsync(
        TeensyUploadTool tool,
        string firmwarePath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(firmwarePath))
        {
            throw new FileNotFoundException("Firmware HEX file was not found.", firmwarePath);
        }

        string extension = Path.GetExtension(firmwarePath);
        if (!string.Equals(extension, ".hex", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Teensy firmware uploads require a .hex file.");
        }

        ProcessStartInfo start = new()
        {
            FileName = tool.ExecutablePath,
            WorkingDirectory = tool.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        if (tool.Kind == TeensyUploadToolKind.PostCompile)
        {
            string firmwareName = Path.GetFileNameWithoutExtension(firmwarePath);
            string firmwareDirectory = Path.GetDirectoryName(firmwarePath) ?? Environment.CurrentDirectory;
            start.ArgumentList.Add($"-file={firmwareName}");
            start.ArgumentList.Add($"-path={firmwareDirectory}");
            start.ArgumentList.Add($"-tools={tool.WorkingDirectory}");
            start.ArgumentList.Add($"-board={TeensyBoard}");
            start.ArgumentList.Add("-reboot");
        }
        else
        {
            start.ArgumentList.Add($"--mcu={TeensyBoard}");
            start.ArgumentList.Add("-w");
            start.ArgumentList.Add("-s");
            start.ArgumentList.Add("-v");
            start.ArgumentList.Add(firmwarePath);
        }

        using Process process = new()
        {
            StartInfo = start,
        };
        if (!process.Start())
        {
            throw new InvalidOperationException($"Could not start {tool.ExecutablePath}.");
        }

        Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        string standardOutput = await output.ConfigureAwait(false);
        string standardError = await error.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Teensy firmware upload failed with exit code {process.ExitCode}.{Environment.NewLine}{standardError}{standardOutput}".Trim());
        }
    }

    private TeensyUploadTool? FindUploadTool(string firmwareDirectory)
    {
        return FindExecutable("teensy_post_compile.exe", firmwareDirectory, environment.BaseDirectory) is string postCompilePath
            ? new(postCompilePath, TeensyUploadToolKind.PostCompile)
            : FindExecutable("teensy_loader_cli.exe", firmwareDirectory, environment.BaseDirectory) is string cliPath
                ? new(cliPath, TeensyUploadToolKind.LoaderCli)
                : null;
    }

    private static string? FindExecutable(string fileName, params string[] preferredDirectories)
    {
        foreach (string directory in preferredDirectories)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            string candidate = Path.Combine(directory, fileName);
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        string? platformIoTool = FindPlatformIoTool(fileName);
        if (platformIoTool is not null)
        {
            return platformIoTool;
        }

        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (string directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(directory.Trim(), fileName);
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    private static string? FindPlatformIoTool(string fileName)
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(profile))
        {
            return null;
        }

        string candidate = Path.Combine(profile, ".platformio", "packages", "tool-teensy", fileName);
        return File.Exists(candidate) ? Path.GetFullPath(candidate) : null;
    }

    private string ResolveFirmwareDirectory(string? firmwareDirectory)
    {
        string settingsDirectory = Path.GetDirectoryName(settingsFile.Path) ?? environment.BaseDirectory;
        if (string.IsNullOrWhiteSpace(firmwareDirectory))
        {
            return settingsDirectory;
        }

        string expanded = Environment.ExpandEnvironmentVariables(firmwareDirectory.Trim());
        return Path.IsPathFullyQualified(expanded)
            ? Path.GetFullPath(expanded)
            : Path.GetFullPath(expanded, settingsDirectory);
    }

    private string ResolveFirmwarePath(string firmwareDirectory)
    {
        // Use exact build artifact names only. Uploading the first *.hex in a directory can pick stale firmware.
        string[] candidates =
        [
            Path.Combine(firmwareDirectory, ProductMetadata.TeensyFirmwareFileName),
            Path.Combine(environment.BaseDirectory, ProductMetadata.TeensyFirmwareFileName),
        ];

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        throw new FileNotFoundException(
            $"Teensy firmware was not found. Expected {ProductMetadata.TeensyFirmwareFileName} beside the app or in the firmware directory.",
            Path.Combine(firmwareDirectory, ProductMetadata.TeensyFirmwareFileName));
    }

    private readonly record struct TeensyUploadTool(string ExecutablePath, TeensyUploadToolKind Kind)
    {
        public string WorkingDirectory { get; } =
            Path.GetDirectoryName(ExecutablePath) ?? Environment.CurrentDirectory;
    }

    private enum TeensyUploadToolKind
    {
        PostCompile,
        LoaderCli,
    }
}
