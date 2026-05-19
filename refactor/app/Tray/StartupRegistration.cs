using Microsoft.Win32;

namespace VirtualMouse.Tray;

internal static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "VirtualMouse.Refactor.Tray";

    public static bool IsEnabled()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string value &&
            string.Equals(value, Quote(Application.ExecutablePath), StringComparison.OrdinalIgnoreCase);
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
}
