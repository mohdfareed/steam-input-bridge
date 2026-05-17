namespace VirtualMouse.Protocol;

public sealed class ConnectionOptions
{
    public const string SectionName = "Connection";

    public string PipeName { get; set; } = "VirtualMouse.Refactor";
    public int ReconnectDelayMilliseconds { get; set; } = 1000;
    public int KeepAliveMilliseconds { get; set; } = 1000;
}
