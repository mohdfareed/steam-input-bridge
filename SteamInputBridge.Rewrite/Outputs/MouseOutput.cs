namespace SteamInputBridge.Outputs;

/// <summary>Virtual pointer output selected by a profile.</summary>
public enum MouseOutput
{
    /// <summary>No virtual pointer output.</summary>
    None,

    /// <summary>VIIPER virtual mouse output.</summary>
    Viiper,

    /// <summary>Teensy hardware mouse output.</summary>
    Teensy,
}
