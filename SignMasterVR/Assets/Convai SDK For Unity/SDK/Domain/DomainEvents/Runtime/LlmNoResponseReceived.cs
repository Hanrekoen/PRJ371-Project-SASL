using System;

namespace Convai.Domain.DomainEvents.Runtime
{
    /// <summary>
    ///     Domain event raised when the backend LLM explicitly decides not to respond to user input.
    /// </summary>
    public readonly struct LlmNoResponseReceived
    {
        /// <summary>The Convai character ID associated with the no-response event.</summary>
        public string CharacterId { get; }

        /// <summary>The transport-layer participant ID associated with the no-response event.</summary>
        public string ParticipantId { get; }

        /// <summary>Optional reason for the no-response decision (e.g., "abstain").</summary>
        public string Reason { get; }

        /// <summary>When the event occurred (UTC).</summary>
        public DateTime Timestamp { get; }

        /// <summary>Creates a new LlmNoResponseReceived event.</summary>
        public LlmNoResponseReceived(string characterId, string participantId, string reason, DateTime timestamp)
        {
            CharacterId = characterId ?? string.Empty;
            ParticipantId = participantId ?? string.Empty;
            Reason = reason ?? string.Empty;
            Timestamp = timestamp;
        }

        /// <summary>Creates a LlmNoResponseReceived event with the current UTC timestamp.</summary>
        public static LlmNoResponseReceived Create(string characterId, string participantId, string reason) =>
            new(characterId, participantId, reason, DateTime.UtcNow);
    }
}
