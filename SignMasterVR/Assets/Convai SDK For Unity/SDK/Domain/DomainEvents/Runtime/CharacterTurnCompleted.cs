using System;

namespace Convai.Domain.DomainEvents.Runtime
{
    /// <summary>
    ///     Domain event raised when a character finishes its full speaking turn.
    ///     Surfaces the backend's <c>bot-turn-completed</c> signal via EventHub.
    /// </summary>
    /// <remarks>
    ///     This event indicates that all audio for the character's current response has been played.
    ///     It is more reliable than speech-stop events, which can fire during natural pauses.
    /// </remarks>
    public readonly struct CharacterTurnCompleted
    {
        /// <summary>
        ///     The Convai character ID whose turn completed.
        /// </summary>
        public string CharacterId { get; }

        /// <summary>
        ///     The transport-layer participant ID for the character.
        /// </summary>
        public string ParticipantId { get; }

        /// <summary>
        ///     Whether the turn ended because the user interrupted the bot.
        /// </summary>
        public bool WasInterrupted { get; }

        /// <summary>
        ///     When the turn completed signal was received (UTC).
        /// </summary>
        public DateTime Timestamp { get; }

        /// <summary>
        ///     Creates a new <see cref="CharacterTurnCompleted" /> event.
        /// </summary>
        public CharacterTurnCompleted(string characterId, string participantId, bool wasInterrupted, DateTime timestamp)
        {
            CharacterId = characterId ?? string.Empty;
            ParticipantId = participantId ?? string.Empty;
            WasInterrupted = wasInterrupted;
            Timestamp = timestamp;
        }

        /// <summary>
        ///     Creates a new event with the current UTC timestamp.
        /// </summary>
        public static CharacterTurnCompleted Create(string characterId, string participantId, bool wasInterrupted) =>
            new(characterId, participantId, wasInterrupted, DateTime.UtcNow);
    }
}
