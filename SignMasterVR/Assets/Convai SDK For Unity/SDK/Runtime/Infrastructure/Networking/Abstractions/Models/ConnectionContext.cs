using System;

namespace Convai.Infrastructure.Networking.Models
{
    /// <summary>
    ///     Tracks the last known good connection info for reconnect scenarios.
    ///     Immutable after creation; create a new instance to update values.
    /// </summary>
    public sealed class ConnectionContext
    {
        /// <summary>
        ///     Creates a new connection context.
        /// </summary>
        public ConnectionContext(
            string roomName,
            string characterSessionId,
            string sessionId,
            string characterId,
            DateTime connectedAtUtc,
            DateTime? disconnectedAtUtc = null)
        {
            RoomName = roomName;
            CharacterSessionId = characterSessionId;
            SessionId = sessionId;
            CharacterId = characterId;
            ConnectedAtUtc = connectedAtUtc;
            DisconnectedAtUtc = disconnectedAtUtc;
        }

        /// <summary>
        ///     The LiveKit/Daily room name from the last successful connection.
        /// </summary>
        public string RoomName { get; }

        /// <summary>
        ///     The character session ID for conversation continuity (resume-if-possible).
        /// </summary>
        public string CharacterSessionId { get; }

        /// <summary>
        ///     The session ID from the last successful connection.
        /// </summary>
        public string SessionId { get; }

        /// <summary>
        ///     UTC timestamp when the connection was established.
        /// </summary>
        public DateTime ConnectedAtUtc { get; }

        /// <summary>
        ///     UTC timestamp when the disconnection occurred.
        ///     Null if still connected or never disconnected.
        /// </summary>
        public DateTime? DisconnectedAtUtc { get; }

        /// <summary>
        ///     The character ID this context applies to.
        /// </summary>
        public string CharacterId { get; }

        /// <summary>
        ///     Creates an empty/invalid context.
        /// </summary>
        public static ConnectionContext Empty => new(null, null, null, null, DateTime.MinValue);

        /// <summary>
        ///     Returns true if this context has valid room information.
        /// </summary>
        public bool HasValidRoom => !string.IsNullOrEmpty(RoomName);

        /// <summary>
        ///     Returns true if this context has a character session ID for resume.
        /// </summary>
        public bool CanResumeSession => !string.IsNullOrEmpty(CharacterSessionId);

        /// <summary>
        ///     Creates a copy of this context with the disconnection timestamp set.
        /// </summary>
        public ConnectionContext WithDisconnection(DateTime disconnectedAtUtc)
        {
            return new ConnectionContext(
                RoomName,
                CharacterSessionId,
                SessionId,
                CharacterId,
                ConnectedAtUtc,
                disconnectedAtUtc);
        }

        /// <summary>
        ///     Checks if the room is still valid for rejoin based on the TTL.
        /// </summary>
        /// <param name="ttlSeconds">Time-to-live in seconds for room rejoin eligibility.</param>
        /// <returns>True if the room can be rejoined.</returns>
        public bool IsRoomValidForRejoin(double ttlSeconds)
        {
            if (!HasValidRoom || !DisconnectedAtUtc.HasValue)
                return false;

            TimeSpan elapsed = DateTime.UtcNow - DisconnectedAtUtc.Value;
            return elapsed.TotalSeconds <= ttlSeconds;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            string roomPart = HasValidRoom ? $"Room={RoomName}" : "Room=(none)";
            string sessionPart =
                CanResumeSession ? $"CharacterSession={CharacterSessionId}" : "CharacterSession=(none)";
            string disconnectPart = DisconnectedAtUtc.HasValue
                ? $"DisconnectedAt={DisconnectedAtUtc.Value:HH:mm:ss}"
                : "Connected";
            return $"[ConnectionContext {roomPart}, {sessionPart}, {disconnectPart}]";
        }
    }
}
