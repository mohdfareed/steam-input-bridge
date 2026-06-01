using System;
using Microsoft.Win32;

namespace SteamInputBridge.App;

internal static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SteamInputBridge.Tray";

    // MARK: Publics
    // ========================================================================

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
            string processPath = Environment.ProcessPath ??
                throw new InvalidOperationException("Current executable path is unavailable.");
            key.SetValue(ValueName, Quote(processPath));
            return;
        }

        key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    // MARK: Implementation
    // ========================================================================

    private static bool IsCurrentExecutable(string value)
    {
        string expected = Quote(Environment.ProcessPath ?? string.Empty);
        string trimmed = value.Trim();
        return string.Equals(trimmed, expected, StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith(expected + " ", StringComparison.OrdinalIgnoreCase);
    }

    private static string Quote(string path)
    {
        return "\"" + path + "\"";
    }
}
