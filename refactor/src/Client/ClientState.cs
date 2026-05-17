using System;

namespace VirtualMouse.Client;

// MARK: Client State
// ============================================================================

public enum ClientConnectionState
{
    Disconnected,
    Connecting,
    Connected,
}

public sealed class ClientConnectionChangedEventArgs(ClientConnectionState state, Guid? clientId) : EventArgs
{
    public ClientConnectionState State { get; } = state;

    public Guid? ClientId { get; } = clientId;
}
