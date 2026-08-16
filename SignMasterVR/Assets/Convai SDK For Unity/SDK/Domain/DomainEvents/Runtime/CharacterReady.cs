using System;

namespace Convai.Domain.DomainEvents.Runtime
{
    /// <summary>
    ///     Domain event raised when a character signals it is ready to interact.
    ///     Surfaces bot-ready RTVI message via EventHub.
    /// </summary>
    /// <remarks>
    ///     This event is published via EventHub whenever a bot-ready message is received from the server.
    ///     It indicates that a character is ready to begin interaction.
    ///     Typically use EventDeliveryPolicy.MainThread for UI updates and
    ///     EventDeliveryPolicy.Immediate for lightweight logging or analytics.
    /// </remarks>
    public readonly struct CharacterReady
    {
        /// <summary>
        ///     The Convai character ID that is ready.
        ///     This is the primary identifier for matching to ConvaiCharacter instances.
        /// </summary>
        public string CharacterId { get; }

        /// <summary>
        ///     The transport-layer participant ID of the character that is ready.
        ///     Preserved for debugging and edge cases where characterId resolution fails.
        /// </summary>
        public string ParticipantId { get; }

        /// <summary>
        ///     When the character-ready signal was received (UTC).
        /// </summary>
        public DateTime Timestamp { get; }

        /// <summary>
        ///     Creates a new CharacterReady event.
        /// </summary>
        public CharacterReady(string characterId, string participantId, DateTime timestamp)
        {
            CharacterId = characterId ?? string.Empty;
            ParticipantId = participantId ?? string.Empty;
            Timestamp = timestamp;
        }

        /// <summary>
        ///     Creates a CharacterReady event with the current UTC timestamp.
        /// </summary>
        /// <param name="characterId">The Convai character ID</param>
        /// <param name="participantId">The transport-layer participant ID</param>
        /// <returns>A new CharacterReady event</returns>
        public static CharacterReady Create(string characterId, string participantId) =>
            new(characterId, participantId, DateTime.UtcNow);
    }
}
