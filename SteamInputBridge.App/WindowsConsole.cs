using System;
using System.IO;
using static Vanara.PInvoke.Kernel32;

namespace SteamInputBridge.App;

internal static class WindowsConsole
{
    private static StreamWriter? _output;
    private static StreamWriter? _error;
    private static StreamReader? _input;

    public static void AttachForCli()
    {
        if (!AttachConsole(ATTACH_PARENT_PROCESS))
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
}
