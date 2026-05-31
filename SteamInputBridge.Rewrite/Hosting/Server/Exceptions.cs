using System;

namespace SteamInputBridge.Hosting.Server;

/// <summary>Thrown when another Steam Input Bridge server already owns the local server instance.</summary>
public sealed class ServerAlreadyRunningException : InvalidOperationException
{
    private const string DefaultMessage = "Steam Input Bridge server is already running.";

    /// <summary>Creates the exception with the default message.</summary>
    public ServerAlreadyRunningException()
        : base(DefaultMessage)
    {
    }

    /// <summary>Creates the exception with a custom message.</summary>
    public ServerAlreadyRunningException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a custom message and inner exception.</summary>
    public ServerAlreadyRunningException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
