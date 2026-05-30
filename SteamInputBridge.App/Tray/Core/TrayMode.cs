using System;
using System.Windows;

namespace SteamInputBridge.App.Tray.Core;

internal static class TrayMode
{
    [STAThread]
    public static int Run()
    {
        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetColorMode(System.Windows.Forms.SystemColorMode.System);
        System.Windows.Application app = new()
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown,
        };

        using AppContext context = AppContext.Create();
        context.Start();
        return app.Run();
    }
}
