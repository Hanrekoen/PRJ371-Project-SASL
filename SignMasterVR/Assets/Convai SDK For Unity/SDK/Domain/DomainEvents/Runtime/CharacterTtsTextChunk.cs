using System;

namespace Convai.Domain.DomainEvents.Runtime
{
    /// <summary>
    ///     Domain event raised when a TTS text chunk is received from a character.
    ///     Surfaces bot-tts-text RTVI message via EventHub.
    /// </summary>
    /// <remarks>
    ///     This event is published via EventHub whenever a bot-tts-text message is received from the server.
    ///     It contains text chunks that will be synthesized to speech by the TTS system.
    ///     Typically use EventDeliveryPolicy.MainThread for UI updates and
    ///     EventDeliveryPolicy.Immediate for lightweight logging or analytics.
    /// </remarks>
    public readonly struct CharacterTtsTextChunk
    {
        /// <summary>
        ///     The participant ID of the character sending the TTS text.
        /// </summary>
        public string ParticipantId { get; }

        /// <summary>
        ///     The TTS text chunk content.
        /// </summary>
        public string Text { get; }

        /// <summary>
        ///     When the TTS text chunk was received (UTC).
        /// </summary>
        public DateTime Timestamp { get; }

        /// <summary>
        ///     Optional chunk sequence number for ordering multiple chunks.
        /// </summary>
        public int? ChunkIndex { get; }

        /// <summary>
        ///     Whether this is the final chunk in a sequence.
        /// </summary>
        public bool IsFinal { get; }

        /// <summary>
        ///     Creates a new CharacterTtsTextChunk event.
        /// </summary>
        public CharacterTtsTextChunk(
            string participantId,
            string text,
            DateTime timestamp,
            int? chunkIndex = null,
            bool isFinal = false)
        {
            ParticipantId = participantId ?? throw new ArgumentNullException(nameof(participantId));
            Text = text ?? string.Empty;
            Timestamp = timestamp;
            ChunkIndex = chunkIndex;
            IsFinal = isFinal;
        }

        /// <summary>
        ///     Creates a CharacterTtsTextChunk event with the current UTC timestamp.
        /// </summary>
        /// <param name="participantId">The participant ID of the character</param>
        /// <param name="text">The TTS text chunk</param>
        /// <param name="chunkIndex">Optional chunk sequence number</param>
        /// <param name="isFinal">Whether this is the final chunk</param>
        /// <returns>A new CharacterTtsTextChunk event</returns>
        public static CharacterTtsTextChunk Create(
            string participantId,
            string text,
            int? chunkIndex = null,
            bool isFinal = false) =>
            new(participantId, text, DateTime.UtcNow, chunkIndex, isFinal);

        /// <summary>
        ///     Checks if the text is empty.
        /// </summary>
        public bool IsEmpty => string.IsNullOrWhiteSpace(Text);
    }
}
