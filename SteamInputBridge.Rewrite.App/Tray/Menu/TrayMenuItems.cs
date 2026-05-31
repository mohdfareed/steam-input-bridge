using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Forms;

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

    public static ToolStripMenuItem ActionItem(string text, Action callback)
    {
        ToolStripMenuItem item = Item(text);
        item.Click += (_, _) => callback();
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

    public static string Number(int? value)
    {
        return value?.ToString(CultureInfo.InvariantCulture) ?? "None";
    }
    public static string Number(uint value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    public static string Number(uint? value)
    {
        return value?.ToString(CultureInfo.InvariantCulture) ?? "None";
    }

    public static string Enabled(bool enabled)
    {
        return enabled ? "Enabled" : "Disabled";
    }

    public static string Active(bool active)
    {
        return active ? "Active" : "Inactive";
    }

    public static string YesNo(bool value)
    {
        return value ? "Yes" : "No";
    }

    public static string Output<T>(T? value) where T : struct
    {
        return value.HasValue ? value.Value.ToString() ?? string.Empty : "None";
    }

    public static void SetValue(ToolStripMenuItem item, string value)
    {
        item.ShortcutKeyDisplayString = value;
        item.ShowShortcutKeys = !string.IsNullOrWhiteSpace(value);
    }

    // MARK: Images
    // ========================================================================

    public static void SetGreenCheckMark(ToolStripMenuItem item, bool visible)
    {
        SetCheckMark(item, visible, Color.FromArgb(32, 160, 80));
    }

    public static void SetCheckMark(ToolStripMenuItem item, bool visible)
    {
        SetCheckMark(item, visible, SystemColors.MenuText);
    }

    private static void SetCheckMark(ToolStripMenuItem item, bool visible, Color color)
    {
        item.Image?.Dispose();
        item.Image = null;

        if (!visible)
        {
            return;
        }

        item.Image = CheckMark(color);
    }

    private static Bitmap CheckMark(Color color)
    {
        Bitmap image = new(16, 16);
        using Graphics graphics = Graphics.FromImage(image);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using Pen pen = new(color, 2.4f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };

        graphics.DrawLines(pen, [new(3, 8), new(7, 12), new(13, 4)]);
        return image;
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
