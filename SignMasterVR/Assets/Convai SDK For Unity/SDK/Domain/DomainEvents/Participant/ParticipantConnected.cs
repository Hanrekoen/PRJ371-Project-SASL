using System;

namespace Convai.Domain.DomainEvents.Participant
{
    /// <summary>
    ///     Domain event raised when a participant joins the Convai session.
    /// </summary>
    public readonly struct ParticipantConnected
    {
        /// <summary>
        ///     Information about the participant that connected.
        /// </summary>
        public ParticipantInfo Participant { get; }

        /// <summary>
        ///     When the participant connected (UTC).
        /// </summary>
        public DateTime Timestamp { get; }

        /// <summary>
        ///     Creates a new ParticipantConnected event.
        /// </summary>
        public ParticipantConnected(ParticipantInfo participant, DateTime timestamp)
        {
            Participant = participant;
            Timestamp = timestamp;
        }

        /// <summary>
        ///     Creates a ParticipantConnected event with the current UTC timestamp.
        /// </summary>
        public static ParticipantConnected Create(ParticipantInfo participant) => new(participant, DateTime.UtcNow);

        /// <summary>
        ///     Creates a ParticipantConnected event for a Convai character.
        /// </summary>
        public static ParticipantConnected ForCharacter(string participantId, string identity, string displayName) =>
            Create(ParticipantInfo.ForCharacter(participantId, identity, displayName));
    }
}
