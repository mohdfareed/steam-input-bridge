using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SteamInputBridge.App.Host;
using SteamInputBridge.Hosting.Server;
using SteamInputBridge.Outputs.Teensy;
using SteamInputBridge.Settings;
using SteamInputBridge.Steam;

namespace SteamInputBridge.App.Tray.Menu;

internal sealed class TrayActions(
    IHost server,
    AppEnvironment environment,
    SettingsFile settingsFile,
    BridgeService bridgeService,
    NotifyIcon tray,
    CancellationToken cancellationToken)
{
    private const string AppName = "Steam Input Bridge";
    private const string TeensyBoard = "TEENSY40";

    // MARK: Publics
    // ========================================================================

    public static bool StartupEnabled => StartupRegistration.IsEnabled();

    public async Task OpenDesktopSteamInputConfigAsync()
    {
        SteamInputClient steam = new();
        await steam.OpenSteamConfigAsync(SteamInputClient.DesktopConfigAppId, cancellationToken).ConfigureAwait(true);
    }

    public async Task OpenSteamInputConfigAsync(uint appId)
    {
        SteamInputClient steam = new();
        await steam.OpenSteamConfigAsync(appId, cancellationToken).ConfigureAwait(true);
    }

    public void ExportSrmManifest()
    {
        SettingsService settings = server.Services.GetRequiredService<SettingsService>();
        string manifestPath = SteamRomManagerExport.WriteManifest(settings, settingsFile, environment.ExecutablePath);
        tray.ShowBalloonTip(5000, AppName, $"Exported SRM manifest to {manifestPath}.", ToolTipIcon.Info);
    }

    public void OpenSettings()
    {
        OpenFile(settingsFile.Path);
    }

    public void OpenLogs()
    {
        string? directory = Path.GetDirectoryName(environment.LogPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            _ = Directory.CreateDirectory(directory);
        }

        using (File.Open(environment.LogPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite))
        {
        }

        OpenFile(environment.LogPath);
    }

    public async Task UploadTeensyFirmwareAsync()
    {
        SettingsService settings = server.Services.GetRequiredService<SettingsService>();
        string firmwareDirectory = ResolveFirmwareDirectory(settings.Current.Teensy.FirmwareDirectory);
#pragma warning disable CA1303
        using OpenFileDialog dialog = new()
        {
            Title = "Upload Teensy firmware",
            Filter = "Teensy firmware (*.hex)|*.hex|All files (*.*)|*.*",
            CheckFileExists = true,
            InitialDirectory = Directory.Exists(firmwareDirectory) ? firmwareDirectory : environment.BaseDirectory,
        };
#pragma warning restore CA1303

        if (dialog.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        string firmwarePath = Path.GetFullPath(dialog.FileName);
        TeensyUploadTool tool = FindUploadTool(firmwareDirectory) ??
            throw new FileNotFoundException(
                "Teensy uploader was not found. Put teensy_post_compile.exe or teensy_loader_cli.exe in the firmware directory, or install PlatformIO's Teensy tools.",
                Path.Combine(firmwareDirectory, "teensy_post_compile.exe"));

        TeensyMouseOutputService teensy = server.Services.GetRequiredService<TeensyMouseOutputService>();
        using IDisposable pause = teensy.PauseConnection();
        await RunUploadToolAsync(tool, firmwarePath, cancellationToken).ConfigureAwait(true);
    }

    public static void ToggleStartup()
    {
        StartupRegistration.SetEnabled(!StartupRegistration.IsEnabled());
    }

    public Task StopClientAsync(Guid connectionId)
    {
        return bridgeService.StopClientAsync(connectionId);
    }

    // MARK: Implementation
    // ========================================================================

    private static void OpenFile(string path)
    {
        string fullPath = Path.GetFullPath(path);
        ProcessStartInfo start = new()
        {
            FileName = fullPath,
            UseShellExecute = true,
        };
        _ = Process.Start(start) ?? throw new InvalidOperationException($"Could not open {fullPath}.");
    }

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
        _ = process.Start();

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

            candidate = Path.Combine(directory, "Firmware", fileName);
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
