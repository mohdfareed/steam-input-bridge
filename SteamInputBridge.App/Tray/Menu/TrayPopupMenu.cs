using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Windows.Forms;

namespace SteamInputBridge.App.Tray.Menu;

internal sealed class TrayMenuItem
{
    private TrayMenuItem(
        string text,
        string? value,
        Action? callback,
        bool enabled,
        bool isChecked,
        bool isSeparator,
        IReadOnlyList<TrayMenuItem> children)
    {
        Text = text;
        Value = value;
        Callback = callback;
        Enabled = enabled;
        IsChecked = isChecked;
        IsSeparator = isSeparator;
        Children = children;
    }

    public string Text { get; }

    public string? Value { get; }

    public Action? Callback { get; }

    public bool Enabled { get; }

    public bool IsChecked { get; }

    public bool IsSeparator { get; }

    public IReadOnlyList<TrayMenuItem> Children { get; }

    public static TrayMenuItem Separator { get; } = new(
        string.Empty,
        null,
        null,
        enabled: false,
        isChecked: false,
        isSeparator: true,
        []);

    public static TrayMenuItem Action(
        string text,
        Action callback,
        bool isChecked = false)
    {
        return new TrayMenuItem(text, null, callback, enabled: true, isChecked, isSeparator: false, []);
    }

    public static TrayMenuItem Disabled(string text)
    {
        return new TrayMenuItem(text, null, null, enabled: false, isChecked: false, isSeparator: false, []);
    }

    public static TrayMenuItem Status(string label, string value, bool isChecked = false)
    {
        return new TrayMenuItem(
            label,
            value,
            null,
            enabled: false,
            isChecked,
            isSeparator: false,
            []);
    }

    public static TrayMenuItem Menu(string text, IReadOnlyList<TrayMenuItem> children)
    {
        return new TrayMenuItem(text, null, null, enabled: true, isChecked: false, isSeparator: false, children);
    }

    public static TrayMenuItem MenuStatus(string label, string value, IReadOnlyList<TrayMenuItem> children, bool isChecked = false)
    {
        return new TrayMenuItem(label, value, null, enabled: true, isChecked: isChecked, isSeparator: false, children);
    }
}

internal static class TrayPopupMenu
{
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The modeless menu disposes itself from its Closed event.")]
    public static void Show(Point location, IReadOnlyList<TrayMenuItem> items, Action? closed)
    {
        ContextMenuStrip menu = new()
        {
            ShowCheckMargin = true,
            ShowImageMargin = false,
        };

        foreach (TrayMenuItem item in items)
        {
            _ = menu.Items.Add(CreateItem(item));
        }

        menu.Closed += (_, _) =>
        {
            closed?.Invoke();
            menu.Dispose();
        };
        menu.Show(location);
    }

    private static ToolStripItem CreateItem(TrayMenuItem item)
    {
        if (item.IsSeparator)
        {
            return new ToolStripSeparator();
        }

        ToolStripMenuItem menuItem = new(item.Text)
        {
            Checked = item.IsChecked,
            Enabled = item.Enabled,
            ShortcutKeyDisplayString = item.Value ?? string.Empty,
        };
        if (menuItem.DropDown is ToolStripDropDownMenu dropDown)
        {
            dropDown.ShowCheckMargin = true;
            dropDown.ShowImageMargin = false;
        }

        foreach (TrayMenuItem child in item.Children)
        {
            _ = menuItem.DropDownItems.Add(CreateItem(child));
        }

        if (item.Callback is not null)
        {
            menuItem.Click += (_, _) => item.Callback();
        }

        return menuItem;
    }
}
