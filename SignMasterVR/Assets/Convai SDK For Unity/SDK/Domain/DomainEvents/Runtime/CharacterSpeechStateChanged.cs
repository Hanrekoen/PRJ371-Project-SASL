using System;

namespace Convai.Domain.DomainEvents.Runtime
{
    /// <summary>
    ///     Domain event raised when a character's speaking state changes.
    ///     Surfaces character speech state via EventHub.
    /// </summary>
    /// <remarks>
    ///     This event is published via EventHub whenever a character starts or stops speaking.
    ///     Typically use EventDeliveryPolicy.MainThread for animation or UI updates and
    ///     EventDeliveryPolicy.Immediate for lightweight logging or analytics.
    /// </remarks>
    public readonly struct CharacterSpeechStateChanged
    {
        /// <summary>
        ///     The character's unique identifier.
        /// </summary>
        public string CharacterId { get; }

        /// <summary>
        ///     Whether the character is currently speaking.
        /// </summary>
        public bool IsSpeaking { get; }

        /// <summary>
        ///     When the speech state changed (UTC).
        /// </summary>
        public DateTime Timestamp { get; }

        /// <summary>
        ///     Optional audio clip ID or utterance ID associated with this speech.
        /// </summary>
        public string UtteranceId { get; }

        /// <summary>
        ///     Creates a new CharacterSpeechStateChanged event.
        /// </summary>
        public CharacterSpeechStateChanged(
            string characterId,
            bool isSpeaking,
            DateTime timestamp,
            string utteranceId = null)
        {
            CharacterId = characterId;
            IsSpeaking = isSpeaking;
            Timestamp = timestamp;
            UtteranceId = utteranceId;
        }

        /// <summary>
        ///     Creates a CharacterSpeechStateChanged event with the current UTC timestamp.
        /// </summary>
        /// <param name="characterId">The character's unique identifier</param>
        /// <param name="isSpeaking">Whether the character is speaking</param>
        /// <param name="utteranceId">Optional utterance ID</param>
        /// <returns>A new CharacterSpeechStateChanged event</returns>
        public static CharacterSpeechStateChanged Create(
            string characterId,
            bool isSpeaking,
            string utteranceId = null)
        {
            return new CharacterSpeechStateChanged(
                characterId,
                isSpeaking,
                DateTime.UtcNow,
                utteranceId
            );
        }

        /// <summary>
        ///     Creates a "started speaking" event.
        /// </summary>
        /// <param name="characterId">The character's unique identifier</param>
        /// <param name="utteranceId">Optional utterance ID</param>
        /// <returns>A new CharacterSpeechStateChanged event with IsSpeaking=true</returns>
        public static CharacterSpeechStateChanged StartedSpeaking(
            string characterId,
            string utteranceId = null) =>
            Create(characterId, true, utteranceId);

        /// <summary>
        ///     Creates a "stopped speaking" event.
        /// </summary>
        /// <param name="characterId">The character's unique identifier</param>
        /// <param name="utteranceId">Optional utterance ID</param>
        /// <returns>A new CharacterSpeechStateChanged event with IsSpeaking=false</returns>
        public static CharacterSpeechStateChanged StoppedSpeaking(
            string characterId,
            string utteranceId = null) =>
            Create(characterId, false, utteranceId);

        /// <summary>
        ///     Checks if the character is silent (not speaking).
        /// </summary>
        public bool IsSilent => !IsSpeaking;

        /// <summary>
        ///     Checks if this event represents the start of speech.
        /// </summary>
        public bool IsStartOfSpeech => IsSpeaking;

        /// <summary>
        ///     Checks if this event represents the end of speech.
        /// </summary>
        public bool IsEndOfSpeech => !IsSpeaking;
    }
}
