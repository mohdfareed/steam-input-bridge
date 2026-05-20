using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SteamInputBridge.Hosting.Server;
using SteamInputBridge.Settings;

namespace SteamInputBridge.App.Tray;

internal sealed class AppContext : IDisposable
{
    private readonly IHost _app;
    private readonly ServerService _server;
    private readonly Icon _icon = LoadApplicationIcon();
    private readonly NotifyIcon _tray = new();
    private readonly NativeWindow _window = new();
    private readonly DispatcherTimer _refreshTimer;
    private readonly CancellationTokenSource _stop = new();
    private readonly AppMenu _menu;
    private Task? _serverTask;
    private bool _refreshing;
    private string? _serverError;
    private ServerStatus? _status;

    private AppContext(IHost app)
    {
        _app = app;
        _server = app.Services.GetRequiredService<ServerService>();
        string settingsPath = app.Services.GetRequiredService<SettingsFile>().Path;
        string? logDirectory = ResolveLogDirectory(
            settingsPath,
            app.Services.GetRequiredService<ApplicationSettingsService>().Current.Logging.LogDirectory);
        string? logPath = logDirectory is null
            ? null
            : FileLoggingExtensions.ResolveRunLogFilePath(logDirectory);
        _menu = new AppMenu(
            settingsPath,
            logPath,
            ExportSrmManifest,
            RestartApp,
            StopClient,
            ShutdownApp);

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500),
        };
        _refreshTimer.Tick += RefreshStatusAsync;
    }

    public static AppContext Create()
    {
        IHost? app = AppSetup.Create();
        try
        {
            AppContext context = new(app);
            app = null;
            return context;
        }
        finally
        {
            app?.Dispose();
        }
    }

    public void Start()
    {
        _serverTask = Task.Run(RunServerAsync, CancellationToken.None);
        _window.CreateHandle(new CreateParams());
        WindowsThemeSupport.ApplyToWindow(_window.Handle);
        _tray.Icon = _icon;
        _tray.Text = AppText.TrayStarting;
        _tray.Visible = true;
        _tray.MouseUp += ShowMenu;
        _refreshTimer.Start();
        _ = RefreshStatusNowAsync();
    }

    public void Dispose()
    {
        _refreshTimer.Stop();
        _stop.Cancel();
        _tray.MouseUp -= ShowMenu;
        _tray.Visible = false;
        _tray.Dispose();
        _icon.Dispose();

        try
        {
            _ = _serverTask?.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException exception) when (IsExpectedStop(exception))
        {
        }

        _window.DestroyHandle();
        _server.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _stop.Dispose();
        _app.Dispose();
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "The tray UI stays open and reports server startup failures in the menu.")]
    private async Task RunServerAsync()
    {
        try
        {
            await _server.RunAsync(_stop.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _serverError = exception.Message;
        }
    }

    private async void RefreshStatusAsync(object? sender, EventArgs args)
    {
        _ = sender;
        _ = args;
        await RefreshStatusNowAsync().ConfigureAwait(true);
    }

    private async Task RefreshStatusNowAsync()
    {
        if (_refreshing)
        {
            return;
        }

        _refreshing = true;
        try
        {
            if (_serverTask?.IsFaulted == true)
            {
                _serverError = _serverTask.Exception?.GetBaseException().Message;
            }
            else if (_serverTask?.IsCompleted == false)
            {
                _status = await _server.GetStatusAsync().ConfigureAwait(true);
            }

            _tray.Text = AppText.TrayText(_status, _serverError);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void ShowMenu(object? sender, MouseEventArgs args)
    {
        _ = sender;
        if (args.Button == MouseButtons.Right)
        {
            _ = ShowMenuAsync();
        }
    }

    private async Task ShowMenuAsync()
    {
        await RefreshStatusNowAsync().ConfigureAwait(true);
        _menu.Show(Cursor.Position, _window.Handle, _status, _serverError);
    }

    private static bool IsExpectedStop(AggregateException exception)
    {
        return exception.GetBaseException() is OperationCanceledException or ObjectDisposedException;
    }

    private static void ShutdownApp()
    {
        System.Windows.Application.Current.Shutdown();
    }

    private void StopClient(Guid clientId)
    {
        _ = StopClientAsync(clientId);
    }

    private async Task StopClientAsync(Guid clientId)
    {
        await _server.StopClientAsync(clientId).ConfigureAwait(true);
        _status = await _server.GetStatusAsync().ConfigureAwait(true);
        _tray.Text = AppText.TrayText(_status, _serverError);
    }

    private static void RestartApp()
    {
        if (Environment.ProcessPath is not { Length: > 0 } processPath)
        {
            return;
        }

        _ = Process.Start(new ProcessStartInfo
        {
            FileName = processPath,
            Arguments = "tray",
            WorkingDirectory = System.AppContext.BaseDirectory,
            UseShellExecute = false,
        });

        ShutdownApp();
    }

    private static Icon LoadApplicationIcon()
    {
        return Environment.ProcessPath is { Length: > 0 } processPath
            ? Icon.ExtractAssociatedIcon(processPath) ?? (Icon)SystemIcons.Application.Clone()
            : (Icon)SystemIcons.Application.Clone();
    }

    private void ExportSrmManifest()
    {
        SrmExportResult result = SrmExportAction.Export(_app.Services);
        if (result.Exported)
        {
            _tray.ShowBalloonTip(
                3000,
                "Steam Input Bridge",
                $"Exported {result.ProfileCount} SRM profiles.",
                ToolTipIcon.Info);
            return;
        }

        _tray.ShowBalloonTip(
            5000,
            "Steam Input Bridge",
            $"Could not export SRM manifest: {result.Error}",
            ToolTipIcon.Error);
    }

    private static string? ResolveLogDirectory(string settingsPath, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (Path.IsPathFullyQualified(path))
        {
            return path;
        }

        string settingsDirectory = Path.GetDirectoryName(settingsPath) ?? System.AppContext.BaseDirectory;
        return Path.Combine(settingsDirectory, path);
    }
}
