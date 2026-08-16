using System;

namespace Convai.Infrastructure.Networking.Transport
{
    /// <summary>
    ///     Session information after successful connection.
    /// </summary>
    public struct TransportSessionInfo
    {
        /// <summary>Name of the connected room.</summary>
        public string RoomName { get; set; }

        /// <summary>Session identifier.</summary>
        public string SessionId { get; set; }

        /// <summary>Character session identifier.</summary>
        public string CharacterSessionId { get; set; }

        /// <summary>Local participant's unique identifier (SID).</summary>
        public string LocalParticipantId { get; set; }

        /// <summary>Local participant's identity string.</summary>
        public string LocalParticipantIdentity { get; set; }

        /// <summary>Timestamp when connection was established.</summary>
        public DateTime ConnectedAt { get; set; }

        /// <summary>
        ///     Creates a new TransportSessionInfo instance.
        /// </summary>
        public TransportSessionInfo(
            string roomName,
            string sessionId = null,
            string characterSessionId = null,
            string localParticipantId = null,
            string localParticipantIdentity = null,
            DateTime? connectedAt = null)
        {
            RoomName = roomName;
            SessionId = sessionId;
            CharacterSessionId = characterSessionId;
            LocalParticipantId = localParticipantId;
            LocalParticipantIdentity = localParticipantIdentity;
            ConnectedAt = connectedAt ?? DateTime.UtcNow;
        }
    }
}
