namespace Inputs.Sdl;

/// <summary>SDL gamepad connection options.</summary>
public sealed record SdlGamepadOptions
{
    /// <summary>Zero-based SDL gamepad index for buttons and axes.</summary>
    public int DeviceIndex { get; init; }

    /// <summary>Zero-based SDL gamepad index for motion and rumble.</summary>
    public int? MotionDeviceIndex { get; init; }
}

/// <summary>SDL gamepad discovered on the system.</summary>
/// <param name="Index">Zero-based gamepad index.</param>
/// <param name="InstanceId">SDL joystick instance id.</param>
/// <param name="Name">Device name.</param>
/// <param name="SteamHandle">SDL Steam handle; zero means not Steam-routed.</param>
/// <param name="VendorId">USB vendor id when known.</param>
/// <param name="ProductId">USB product id when known.</param>
/// <param name="Path">SDL device path when known.</param>
public sealed record SdlGamepadInfo(
    int Index,
    uint InstanceId,
    string Name,
    ulong SteamHandle,
    ushort VendorId,
    ushort ProductId,
    string? Path)
{
    /// <summary>Gets whether SDL reports this gamepad through Steam Input.</summary>
    public bool IsSteamInput => SteamHandle != 0;

    /// <summary>Gets whether SDL reports a gyro sensor.</summary>
    public bool HasGyro { get; init; }

    /// <summary>Gets whether SDL reports an accelerometer sensor.</summary>
    public bool HasAccelerometer { get; init; }

    /// <summary>Gets whether SDL reports any motion sensor.</summary>
    public bool HasMotion => HasGyro || HasAccelerometer;
}

internal readonly record struct SdlMotionDeviceSelection(
    bool Enabled,
    int? DeviceIndex,
    SdlGamepadInfo? Device,
    bool UsesSecondaryDevice);
