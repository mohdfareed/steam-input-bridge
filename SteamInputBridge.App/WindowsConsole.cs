using System;
using System.IO;
using System.Runtime.InteropServices;

namespace SteamInputBridge.App;

internal static partial class WindowsConsole
{
    private const int AttachParentProcess = -1;
    private static StreamWriter? _output;
    private static StreamWriter? _error;
    private static StreamReader? _input;

    public static void AttachForCli()
    {
        if (!AttachConsole(AttachParentProcess))
        {
            _ = AllocConsole();
        }

        _output = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
        _error = new StreamWriter(Console.OpenStandardError()) { AutoFlush = true };
        _input = new StreamReader(Console.OpenStandardInput());

        Console.SetOut(_output);
        Console.SetError(_error);
        Console.SetIn(_input);
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(int processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllocConsole();
}
