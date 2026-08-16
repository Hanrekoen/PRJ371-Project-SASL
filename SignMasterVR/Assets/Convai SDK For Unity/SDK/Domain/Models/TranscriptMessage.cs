using System;

namespace Convai.Domain.Models
{
    /// <summary>
    ///     Identifies the type of speaker for transcript attribution.
    /// </summary>
    public enum SpeakerType
    {
        /// <summary>Unknown or unspecified speaker type.</summary>
        Unknown = 0,

        /// <summary>An AI character/NPC.</summary>
        Character = 1,

        /// <summary>A human player/user.</summary>
        Player = 2,

        /// <summary>A system message (e.g., moderation, notifications).</summary>
        System = 3
    }

    /// <summary>
    ///     Represents a single transcript message from either a character or player.
    ///     Standardizes transcript data structure across the SDK with multi-user support.
    /// </summary>
    /// <remarks>
    ///     This struct provides a consistent structure for transcript data that can be used
    ///     across services and UI components. It supports multi-user scenarios via the
    ///     ParticipantId and SpeakerType fields.
    ///     The IsFinal flag indicates whether this is an interim (partial) transcript or a final
    ///     (complete) transcript. Interim transcripts are useful for showing real-time feedback,
    ///     while final transcripts should be stored in conversation history.
    ///     Usage:
    ///     <code>
    /// 
    /// TranscriptMessage charMessage = TranscriptMessage.ForCharacter(
    ///     characterId: "char-123",
    ///     characterName: "Alice",
    ///     text: "Hello!",
    ///     isFinal: true
    /// );
    /// 
    /// 
    /// TranscriptMessage playerMessage = TranscriptMessage.ForPlayer(
    ///     text: "Hi there!",
    ///     isFinal: true,
    ///     playerId: "player-local",
    ///     displayName: "John",
    ///     participantId: "PA_xyz123"
    /// );
    /// </code>
    /// </remarks>
    public readonly struct TranscriptMessage
    {
        /// <summary>
        ///     Unique identifier for the player or character (character ID, local player ID, or backend-attributed player ID).
        /// </summary>
        public string PlayerOrCharacterId { get; }

        /// <summary>
        ///     Human-readable name to display in UI.
        /// </summary>
        public string DisplayName { get; }

        /// <summary>
        ///     The transcript text content.
        /// </summary>
        public string Text { get; }

        /// <summary>
        ///     Whether this is a final (complete) transcript or interim (partial).
        /// </summary>
        public bool IsFinal { get; }

        /// <summary>
        ///     When the transcript was generated (UTC).
        /// </summary>
        public DateTime Timestamp { get; }

        /// <summary>
        ///     Optional confidence score from speech recognition (0.0 to 1.0).
        /// </summary>
        public float? Confidence { get; }

        /// <summary>
        ///     LiveKit participant ID (SID) for multi-user attribution.
        /// </summary>
        public string ParticipantId { get; }

        /// <summary>
        ///     Type of speaker (Character, Player, or System).
        /// </summary>
        public SpeakerType SpeakerType { get; }

        /// <summary>
        ///     Creates a new TranscriptMessage with all fields.
        /// </summary>
        public TranscriptMessage(
            string playerOrCharacterId,
            string displayName,
            string text,
            bool isFinal,
            DateTime timestamp,
            float? confidence = null,
            string participantId = null,
            SpeakerType speakerType = SpeakerType.Unknown)
        {
            PlayerOrCharacterId = playerOrCharacterId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            Text = text ?? string.Empty;
            IsFinal = isFinal;
            Timestamp = timestamp;
            Confidence = confidence;
            ParticipantId = participantId ?? string.Empty;
            SpeakerType = speakerType;
        }

        /// <summary>
        ///     Creates a TranscriptMessage with the current UTC timestamp.
        /// </summary>
        /// <param name="playerOrCharacterId">Unique identifier for the player or character</param>
        /// <param name="displayName">Human-readable name to display in UI</param>
        /// <param name="text">The transcript text content</param>
        /// <param name="isFinal">Whether this is a final transcript</param>
        /// <param name="confidence">Optional confidence score (0.0 to 1.0)</param>
        /// <param name="participantId">Optional LiveKit participant ID</param>
        /// <param name="speakerType">Type of speaker</param>
        /// <returns>A new TranscriptMessage</returns>
        public static TranscriptMessage Create(
            string playerOrCharacterId,
            string displayName,
            string text,
            bool isFinal,
            float? confidence = null,
            string participantId = null,
            SpeakerType speakerType = SpeakerType.Unknown)
        {
            return new TranscriptMessage(
                playerOrCharacterId,
                displayName,
                text,
                isFinal,
                DateTime.UtcNow,
                confidence,
                participantId,
                speakerType
            );
        }

        /// <summary>
        ///     Factory method for character transcripts.
        /// </summary>
        /// <param name="characterId">The character's unique ID</param>
        /// <param name="characterName">The character's display name</param>
        /// <param name="text">The transcript text</param>
        /// <param name="isFinal">Whether this is a final transcript</param>
        /// <param name="participantId">Optional LiveKit participant ID</param>
        /// <returns>A new TranscriptMessage for a character</returns>
        public static TranscriptMessage ForCharacter(
            string characterId,
            string characterName,
            string text,
            bool isFinal,
            string participantId = null)
        {
            return new TranscriptMessage(
                characterId,
                characterName,
                text,
                isFinal,
                DateTime.UtcNow,
                participantId: participantId,
                speakerType: SpeakerType.Character
            );
        }

        /// <summary>
        ///     Factory method for player transcripts with multi-user support.
        /// </summary>
        /// <param name="text">The transcript text</param>
        /// <param name="isFinal">Whether this is a final transcript</param>
        /// <param name="playerId">Optional player ID for transcript attribution</param>
        /// <param name="displayName">Optional display name for transcript attribution</param>
        /// <param name="participantId">Optional LiveKit participant ID</param>
        /// <returns>A new TranscriptMessage for a player</returns>
        public static TranscriptMessage ForPlayer(
            string text,
            bool isFinal,
            string playerId = null,
            string displayName = null,
            string participantId = null)
        {
            return new TranscriptMessage(
                playerId ?? "local-player",
                displayName ?? PlayerDisplayName.Default,
                text,
                isFinal,
                DateTime.UtcNow,
                participantId: participantId,
                speakerType: SpeakerType.Player
            );
        }

        /// <summary>
        ///     Checks if this is an interim (partial) transcript.
        /// </summary>
        public bool IsInterim => !IsFinal;

        /// <summary>
        ///     Checks if the transcript text is empty or whitespace.
        /// </summary>
        public bool IsEmpty => string.IsNullOrWhiteSpace(Text);

        /// <summary>
        ///     Gets the word count of the transcript text.
        /// </summary>
        public int WordCount
        {
            get
            {
                if (IsEmpty)
                    return 0;

                return Text.Split(new[] { ' ', '\t', '\n', '\r' },
                    StringSplitOptions.RemoveEmptyEntries).Length;
            }
        }

        /// <summary>
        ///     Checks if the confidence score meets a minimum threshold.
        /// </summary>
        /// <param name="threshold">Minimum confidence threshold (0.0 to 1.0)</param>
        /// <returns>True if confidence is above threshold, or if confidence is not available</returns>
        public bool HasConfidenceAbove(float threshold) => !Confidence.HasValue || Confidence.Value >= threshold;

        /// <summary>
        ///     Creates a formatted string representation suitable for logging.
        /// </summary>
        /// <returns>Formatted string with speaker, text, and metadata</returns>
        public string ToLogString()
        {
            string finalityMarker = IsFinal ? "[FINAL]" : "[INTERIM]";
            string confidenceStr = Confidence.HasValue ? $" (confidence: {Confidence.Value:F2})" : "";
            string speakerTypeStr = SpeakerType != SpeakerType.Unknown ? $" [{SpeakerType}]" : "";
            return $"{finalityMarker}{speakerTypeStr} {DisplayName} ({PlayerOrCharacterId}): \"{Text}\"{confidenceStr}";
        }

        /// <summary>
        ///     Creates a copy of this message with updated text and finality.
        ///     Useful for aggregation scenarios where interim text becomes final.
        /// </summary>
        public TranscriptMessage WithText(string newText, bool isFinal)
        {
            return new TranscriptMessage(
                PlayerOrCharacterId,
                DisplayName,
                newText,
                isFinal,
                Timestamp,
                Confidence,
                ParticipantId,
                SpeakerType
            );
        }

        /// <summary>
        ///     Creates a copy of this message with updated player/character attribution.
        /// </summary>
        public TranscriptMessage WithPlayerOrCharacterInfo(string playerOrCharacterId, string displayName,
            string participantId)
        {
            return new TranscriptMessage(
                playerOrCharacterId ?? PlayerOrCharacterId,
                displayName ?? DisplayName,
                Text,
                IsFinal,
                Timestamp,
                Confidence,
                participantId ?? ParticipantId,
                SpeakerType
            );
        }
    }
}
