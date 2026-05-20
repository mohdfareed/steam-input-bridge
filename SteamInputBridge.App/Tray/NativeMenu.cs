using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;

namespace SteamInputBridge.App.Tray;

internal sealed class NativeMenuItem
{
    private NativeMenuItem(
        string text,
        Action? callback,
        bool enabled,
        bool isChecked,
        bool isSeparator,
        IReadOnlyList<NativeMenuItem> children)
    {
        Text = text;
        Callback = callback;
        Enabled = enabled;
        IsChecked = isChecked;
        IsSeparator = isSeparator;
        Children = children;
    }

    public string Text { get; }

    public Action? Callback { get; }

    public bool Enabled { get; }

    public bool IsChecked { get; }

    public bool IsSeparator { get; }

    public IReadOnlyList<NativeMenuItem> Children { get; }

    public static NativeMenuItem Separator { get; } = new(
        string.Empty,
        null,
        enabled: false,
        isChecked: false,
        isSeparator: true,
        []);

    public static NativeMenuItem Action(
        string text,
        Action callback,
        bool isChecked = false)
    {
        return new NativeMenuItem(text, callback, enabled: true, isChecked, isSeparator: false, []);
    }

    public static NativeMenuItem Disabled(string text)
    {
        return new NativeMenuItem(text, null, enabled: false, isChecked: false, isSeparator: false, []);
    }

    public static NativeMenuItem Status(string label, string value, bool isChecked = false)
    {
        return new NativeMenuItem(
            $"{label}\t{value}",
            null,
            enabled: false,
            isChecked,
            isSeparator: false,
            []);
    }

    public static NativeMenuItem Menu(string text, IReadOnlyList<NativeMenuItem> children)
    {
        return new NativeMenuItem(text, null, enabled: true, isChecked: false, isSeparator: false, children);
    }

    public static NativeMenuItem MenuStatus(string label, string value, IReadOnlyList<NativeMenuItem> children, bool isChecked = false)
    {
        return new NativeMenuItem($"{label}\t{value}", null, enabled: true, isChecked: isChecked, isSeparator: false, children);
    }
}

internal static class NativeMenu
{
    private const uint MfString = 0x0000;
    private const uint MfGrayed = 0x0001;
    private const uint MfChecked = 0x0008;
    private const uint MfPopup = 0x0010;
    private const uint MfSeparator = 0x0800;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmReturNcmd = 0x0100;
    private const int WmNull = 0x0000;

    public static void Show(Point location, IntPtr owner, IReadOnlyList<NativeMenuItem> items)
    {
        Dictionary<int, Action> actions = [];
        int nextId = 1;
        IntPtr menu = CreateMenu(items, actions, ref nextId);
        try
        {
            _ = SetForegroundWindow(owner);
            int command = TrackPopupMenuEx(
                menu,
                TpmRightButton | TpmReturNcmd,
                location.X,
                location.Y,
                owner,
                IntPtr.Zero);
            _ = PostMessage(owner, WmNull, IntPtr.Zero, IntPtr.Zero);

            if (command != 0 && actions.TryGetValue(command, out Action? action))
            {
                action();
            }
        }
        finally
        {
            _ = DestroyMenu(menu);
        }
    }

    private static IntPtr CreateMenu(
        IReadOnlyList<NativeMenuItem> items,
        Dictionary<int, Action> actions,
        ref int nextId)
    {
        IntPtr menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        foreach (NativeMenuItem item in items)
        {
            AppendItem(menu, item, actions, ref nextId);
        }

        return menu;
    }

    private static void AppendItem(
        IntPtr menu,
        NativeMenuItem item,
        Dictionary<int, Action> actions,
        ref int nextId)
    {
        if (item.IsSeparator)
        {
            Append(menu, MfSeparator, UIntPtr.Zero, null);
            return;
        }

        uint flags = MfString |
            (item.Enabled ? 0 : MfGrayed) |
            (item.IsChecked ? MfChecked : 0);

        if (item.Children.Count > 0)
        {
            IntPtr child = CreateMenu(item.Children, actions, ref nextId);
            Append(menu, flags | MfPopup, (UIntPtr)child, item.Text);
            return;
        }

        int id = nextId++;
        if (item.Callback is not null)
        {
            actions[id] = item.Callback;
        }

        Append(menu, flags, (UIntPtr)id, item.Text);
    }

    private static void Append(IntPtr menu, uint flags, UIntPtr id, string? text)
    {
        if (!AppendMenu(menu, flags, id, text))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(
        IntPtr hMenu,
        uint uFlags,
        UIntPtr uIdNewItem,
        string? lpNewItem);

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int TrackPopupMenuEx(
        IntPtr hmenu,
        uint fuFlags,
        int x,
        int y,
        IntPtr hwnd,
        IntPtr lptpm);

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(
        IntPtr hWnd,
        uint msg,
        IntPtr wParam,
        IntPtr lParam);
}
