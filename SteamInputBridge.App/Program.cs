using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SteamInputBridge.App.Host;
using SteamInputBridge.App.Shortcut;
using SteamInputBridge.App.Tray;

namespace SteamInputBridge.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetColorMode(AppErrorDialog.ColorMode);

        try
        {
            return RunAsync(args).ConfigureAwait(false).GetAwaiter().GetResult();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            AppErrorDialog.Show(exception);
            return 1;
        }
    }

    private static async Task<int> RunAsync(string[] args)
    {
        StartViiperIfInstalled();

        if (args.Length == 0)
        {
            args = ["tray"];
        }

        return args[0].Equals("tray", StringComparison.OrdinalIgnoreCase)
            ? TrayMode.Run()
            : args[0].Equals("shortcut", StringComparison.OrdinalIgnoreCase)
            ? args.Length != 2
                ? throw new ArgumentException("shortcut requires a profile id.")
                : await ShortcutMode.RunAsync(args[1], CancellationToken.None).ConfigureAwait(false)
            : throw new ArgumentException($"Unknown app command '{args[0]}'.");
    }

    private static void StartViiperIfInstalled()
    {
        string executablePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VIIPER",
            "viiper.exe");
        if (!File.Exists(executablePath))
        {
            return;
        }

        Process[] runningProcesses = Process.GetProcessesByName("viiper");
        bool isRunning = runningProcesses.Length != 0;
        foreach (Process process in runningProcesses)
        {
            process.Dispose();
        }

        if (isRunning)
        {
            return;
        }

        ProcessStartInfo start = new(executablePath, "server")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(executablePath),
        };

        using Process launchedProcess = Process.Start(start) ??
            throw new InvalidOperationException("Could not start the installed VIIPER server.");
    }
}
