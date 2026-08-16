namespace Convai.Infrastructure.Networking
{
    /// <summary>
    ///     Represents the connection state of a room.
    /// </summary>
    public enum RoomState
    {
        /// <summary>Room is disconnected.</summary>
        Disconnected,

        /// <summary>Room is connecting.</summary>
        Connecting,

        /// <summary>Room is connected and active.</summary>
        Connected,

        /// <summary>Room is reconnecting after a transient failure.</summary>
        Reconnecting
    }
}
