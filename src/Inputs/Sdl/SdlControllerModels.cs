using System;

namespace Inputs.Sdl;

/// <summary>SDL controller source.</summary>
public enum SdlControllerSource
{
    /// <summary>Controller reported directly by SDL.</summary>
    Physical,

    /// <summary>Controller reported through Steam Input.</summary>
    Steam,
}

/// <summary>Stable SDL controller selector.</summary>
public readonly record struct SdlControllerId(string Value)
{
    /// <inheritdoc />
    public override string ToString()
    {
        return Value;
    }

    internal static SdlControllerId Create(SdlControllerInfo controller)
    {
        return controller.Source == SdlControllerSource.Steam && controller.SteamHandle != 0
            ? new SdlControllerId($"steam:{controller.SteamHandle:x16}")
            : !string.IsNullOrWhiteSpace(controller.Path)
            ? new SdlControllerId($"path:{controller.Path}")
            : throw new InvalidOperationException($"SDL controller \"{controller.Name}\" has no stable identity.");
    }
}

/// <summary>SDL controller discovered on the system.</summary>
public sealed record SdlControllerInfo(
    SdlControllerId Id,
    uint InstanceId,
    string Name,
    SdlControllerSource Source,
    ulong SteamHandle,
    ushort VendorId,
    ushort ProductId,
    string? Path,
    bool HasGyro,
    bool HasAccelerometer)
{
    /// <summary>Gets whether SDL reports any motion sensor.</summary>
    public bool HasMotion => HasGyro || HasAccelerometer;

}
