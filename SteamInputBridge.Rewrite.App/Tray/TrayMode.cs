using System;
using System.Threading;
using System.Windows.Forms;

namespace SteamInputBridge.App.Tray;

internal static class TrayMode
{
    private const string TrayMutexName = @"Local\SteamInputBridge.Tray";

    public static int Run()
    {
        using Mutex trayInstance = new(initiallyOwned: true, TrayMutexName, out bool createdTrayInstance);
        bool ownsTrayInstance = createdTrayInstance || TryAcquireExistingTrayInstance(trayInstance);
        if (!ownsTrayInstance)
        {
            return 0;
        }

        try
        {
            Application.EnableVisualStyles();
            Application.SetColorMode(SystemColorMode.System);
            using TrayApplicationContext context = new();
            context.Start();
            Application.Run(context);
            return 0;
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
