using System;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Forms;
using SteamInputBridge.Hosting;
using SteamInputBridge.Settings;

namespace SteamInputBridge.App.Tray.Menu;

internal static class TrayMenuItems
{
    // MARK: Items
    // ========================================================================

    public static ToolStripMenuItem Disabled(string text)
    {
        ToolStripMenuItem item = Item(text);
        item.Enabled = false;
        return item;
    }

    public static ToolStripMenuItem Item(string text, string? shortcut = null)
    {
        return new(text)
        {
            ShortcutKeyDisplayString = shortcut ?? string.Empty,
            ShowShortcutKeys = !string.IsNullOrWhiteSpace(shortcut),
            DropDown = { RenderMode = ToolStripRenderMode.System },
        };
    }

    public static ToolStripMenuItem Menu(string text)
    {
        return new(text)
        {
            DropDown = { RenderMode = ToolStripRenderMode.System },
        };
    }

    public static ToolStripMenuItem ActionItem(string text, Action callback, Action<Exception> onError)
    {
        ToolStripMenuItem item = Item(text);
        item.Click += (_, _) => Run(callback, onError);
        return item;
    }

    public static ToolStripMenuItem ActionItem(string text, Func<Task> callback, Action<Exception> onError)
    {
        ToolStripMenuItem item = Item(text);
        item.Click += (_, _) => _ = RunAsync(callback, onError);
        return item;
    }

    // MARK: Formatters
    // ========================================================================

    public static string Number(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    public static string Number(uint value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    public static string Enabled(bool enabled)
    {
        return enabled ? "Enabled" : "Disabled";
    }

    public static string SteamAppId(GameProfile profile, BridgeClientStatus? client)
    {
        uint? appId = client is null ? profile.SteamAppId : client.SteamAppId;
        return appId.HasValue ? TrayMenuItems.Number(appId.Value) : "None";
    }

    public static string? Output<T>(T? value) where T : struct
    {
        return value.HasValue ? value.Value.ToString() : "None";
    }

    // MARK: Runners
    // ========================================================================

    public static void Run(Action callback, Action<Exception> onError)
    {
        try
        {
            callback();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            onError(exception);
        }
    }

    public static async Task RunAsync(Func<Task> callback, Action<Exception> onError)
    {
        try
        {
            await callback().ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            onError(exception);
        }
    }
}
