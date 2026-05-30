using System;
using System.Threading;
using System.Windows;

namespace SteamInputBridge.App.Tray.Core;

internal static class TrayMode
{
    private const string TrayMutexName = @"Local\SteamInputBridge.Tray";

    [STAThread]
    public static int Run()
    {
        // NotifyIcon has no built-in single-instance behavior. Without this,
        // stale tray processes stack icons and make one click look like several.
        using Mutex trayInstance = new(initiallyOwned: true, TrayMutexName, out bool createdTrayInstance);
        bool ownsTrayInstance = createdTrayInstance || TryAcquireExistingTrayInstance(trayInstance);
        if (!ownsTrayInstance)
        {
            return 0;
        }

        try
        {
            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetColorMode(System.Windows.Forms.SystemColorMode.System);
            Application app = new()
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown,
            };

            using AppContext context = AppContext.Create();
            context.Start();
            return app.Run();
        }
        finally
        {
            trayInstance.ReleaseMutex();
        }
    }

    private static bool TryAcquireExistingTrayInstance(Mutex trayInstance)
    {
        try
        {
            return trayInstance.WaitOne(TimeSpan.Zero);
        }
        catch (AbandonedMutexException)
        {
            return true;
        }
    }
}
