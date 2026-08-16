using System;

namespace Convai.Runtime.Core.MultiParticipant
{
    /// <summary>
    ///     Information about a participant in a multi-participant room.
    /// </summary>
    public readonly struct ParticipantInfo
    {
        /// <summary>
        ///     Creates a new participant info.
        /// </summary>
        public ParticipantInfo(
            string participantId,
            string displayName,
            bool isLocal,
            bool isRoomOwner,
            DateTime joinedAt)
        {
            ParticipantId = participantId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            IsLocal = isLocal;
            IsRoomOwner = isRoomOwner;
            JoinedAt = joinedAt;
        }

        /// <summary>
        ///     Unique identifier for this participant.
        /// </summary>
        public string ParticipantId { get; }

        /// <summary>
        ///     Display name shown to other participants.
        /// </summary>
        public string DisplayName { get; }

        /// <summary>
        ///     Whether this is the local participant.
        /// </summary>
        public bool IsLocal { get; }

        /// <summary>
        ///     Whether this participant is the room owner.
        /// </summary>
        public bool IsRoomOwner { get; }

        /// <summary>
        ///     When this participant joined the room.
        /// </summary>
        public DateTime JoinedAt { get; }
    }

    /// <summary>
    ///     Event raised when a participant joins the room.
    /// </summary>
    public readonly struct ParticipantJoined
    {
        public ParticipantJoined(ParticipantInfo participant)
        {
            Participant = participant;
        }

        public ParticipantInfo Participant { get; }
    }

    /// <summary>
    ///     Event raised when a participant leaves the room.
    /// </summary>
    public readonly struct ParticipantLeft
    {
        public ParticipantLeft(ParticipantInfo participant, string reason)
        {
            Participant = participant;
            Reason = reason ?? string.Empty;
        }

        public ParticipantInfo Participant { get; }
        public string Reason { get; }
    }
}
