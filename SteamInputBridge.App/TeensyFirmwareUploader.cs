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
    private const string TeensyUploaderExecutableName = "teensy_post_compile.exe";

    // MARK: Publics
    // ========================================================================

    public async Task UploadAsync()
    {
        string firmwareDirectory = ResolveFirmwareDirectory(settings.Current.Teensy.FirmwareDirectory);
        string firmwarePath = ResolveFirmwarePath(firmwareDirectory);
        string toolsDirectory = ResolveToolsDirectory();
        string uploaderPath = Path.Combine(toolsDirectory, TeensyUploaderExecutableName);
        if (!File.Exists(uploaderPath))
        {
            throw new FileNotFoundException(
                "Bundled Teensy uploader was not found. Run Scripts\\Deploy-App.ps1 to copy PlatformIO's Teensy tools beside the app.",
                uploaderPath);
        }

        using IDisposable pause = teensy.PauseConnection();
        await RunUploadToolAsync(uploaderPath, toolsDirectory, firmwarePath, cancellationToken).ConfigureAwait(false);
    }

    // MARK: Implementation
    // ========================================================================

    private static async Task RunUploadToolAsync(
        string uploaderPath,
        string toolsDirectory,
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
            FileName = uploaderPath,
            WorkingDirectory = toolsDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        string firmwareName = Path.GetFileNameWithoutExtension(firmwarePath);
        string firmwareDirectory = Path.GetDirectoryName(firmwarePath) ?? Environment.CurrentDirectory;
        start.ArgumentList.Add($"-file={firmwareName}");
        start.ArgumentList.Add($"-path={firmwareDirectory}");
        start.ArgumentList.Add($"-tools={toolsDirectory}");
        start.ArgumentList.Add($"-board={TeensyBoard}");
        start.ArgumentList.Add("-reboot");

        using Process process = new()
        {
            StartInfo = start,
        };
        if (!process.Start())
        {
            throw new InvalidOperationException($"Could not start {uploaderPath}.");
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

    private string ResolveToolsDirectory()
    {
        return Path.GetFullPath(Path.Combine(environment.BaseDirectory, ProductMetadata.TeensyToolsDirectoryName));
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
}
