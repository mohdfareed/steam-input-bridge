using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SteamInputBridge.App.Tray;

internal static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SteamInputBridge.Tray";

    public static bool IsEnabled()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string value && IsCurrentExecutable(value);
    }

    public static void SetEnabled(bool enabled)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true) ??
            throw new InvalidOperationException("Could not open the current-user startup registry key.");

        if (enabled)
        {
            key.SetValue(ValueName, Quote(Application.ExecutablePath));
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    private static string Quote(string path)
    {
        return "\"" + path + "\"";
    }

    private static bool IsCurrentExecutable(string value)
    {
        string expected = Quote(Application.ExecutablePath);
        string trimmed = value.Trim();
        return string.Equals(trimmed, expected, StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith(expected + " ", StringComparison.OrdinalIgnoreCase);
    }
}
