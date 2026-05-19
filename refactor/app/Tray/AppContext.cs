using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VirtualMouse.Hosting;
using VirtualMouse.Settings;

namespace VirtualMouse.Tray;

internal sealed class AppContext : IDisposable
{
    private readonly IHost _app;
    private readonly ServerService _server;
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
        string? logPath = ResolveLogFilePath(
            settingsPath,
            app.Services.GetRequiredService<ApplicationSettingsService>().Current.Logging.LogFile);
        _menu = new AppMenu(
            settingsPath,
            logPath,
            ExportSrmManifest,
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
        _tray.Icon = SystemIcons.Application;
        _tray.Text = AppText.TrayStarting;
        _tray.Visible = true;
        _tray.MouseUp += ShowMenu;
        _refreshTimer.Start();
    }

    public void Dispose()
    {
        _refreshTimer.Stop();
        _stop.Cancel();
        _tray.MouseUp -= ShowMenu;
        _tray.Visible = false;
        _tray.Dispose();

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
            _menu.Show(Cursor.Position, _window.Handle, _status, _serverError);
        }
    }

    private static bool IsExpectedStop(AggregateException exception)
    {
        return exception.GetBaseException() is OperationCanceledException or ObjectDisposedException;
    }

    private static void ShutdownApp()
    {
        System.Windows.Application.Current.Shutdown();
    }

    private void ExportSrmManifest()
    {
        SrmExportResult result = SrmExportAction.Export(_app.Services);
        if (result.Exported)
        {
            _tray.ShowBalloonTip(
                3000,
                "Virtual Mouse",
                $"Exported {result.ProfileCount} SRM profiles.",
                ToolTipIcon.Info);
            return;
        }

        _tray.ShowBalloonTip(
            5000,
            "Virtual Mouse",
            $"Could not export SRM manifest: {result.Error}",
            ToolTipIcon.Error);
    }

    private static string? ResolveLogFilePath(string settingsPath, string? path)
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
