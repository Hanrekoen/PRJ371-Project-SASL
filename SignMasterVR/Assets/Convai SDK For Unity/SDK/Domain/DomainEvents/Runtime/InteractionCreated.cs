using System;

namespace Convai.Domain.DomainEvents.Runtime
{
    /// <summary>
    ///     Domain event raised when backend creates a new interaction context for the current character conversation.
    /// </summary>
    public readonly struct InteractionCreated
    {
        /// <summary>The Convai character ID associated with this interaction.</summary>
        public string CharacterId { get; }

        /// <summary>The transport-layer participant ID associated with this interaction.</summary>
        public string ParticipantId { get; }

        /// <summary>The backend interaction identifier.</summary>
        public string InteractionId { get; }

        /// <summary>The backend character session identifier for this interaction.</summary>
        public string CharacterSessionId { get; }

        /// <summary>When the event occurred (UTC).</summary>
        public DateTime Timestamp { get; }

        /// <summary>Creates a new InteractionCreated event.</summary>
        public InteractionCreated(
            string characterId,
            string participantId,
            string interactionId,
            string characterSessionId,
            DateTime timestamp)
        {
            CharacterId = characterId ?? string.Empty;
            ParticipantId = participantId ?? string.Empty;
            InteractionId = interactionId ?? string.Empty;
            CharacterSessionId = characterSessionId ?? string.Empty;
            Timestamp = timestamp;
        }

        /// <summary>Creates an InteractionCreated event with the current UTC timestamp.</summary>
        public static InteractionCreated Create(
            string characterId,
            string participantId,
            string interactionId,
            string characterSessionId) =>
            new(characterId, participantId, interactionId, characterSessionId, DateTime.UtcNow);
    }
}
