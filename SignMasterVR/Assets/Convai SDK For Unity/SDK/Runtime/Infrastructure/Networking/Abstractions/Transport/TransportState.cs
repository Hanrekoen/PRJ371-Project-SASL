namespace Convai.Infrastructure.Networking.Transport
{
    /// <summary>
    ///     Represents the connection state of a real-time transport.
    /// </summary>
    public enum TransportState
    {
        /// <summary>Not connected to any room.</summary>
        Disconnected = 0,

        /// <summary>Connection attempt in progress.</summary>
        Connecting = 1,

        /// <summary>Successfully connected to room.</summary>
        Connected = 2,

        /// <summary>Reconnecting after transient failure.</summary>
        Reconnecting = 3,

        /// <summary>Disconnect in progress.</summary>
        Disconnecting = 4
    }
}
