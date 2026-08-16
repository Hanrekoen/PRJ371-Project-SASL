using System;

namespace Convai.Domain.DomainEvents.Runtime
{
    /// <summary>
    ///     Domain event raised when a character's emotion state changes.
    ///     Surfaces character emotion updates via EventHub.
    /// </summary>
    /// <remarks>
    ///     This event is published via EventHub whenever the backend sends a bot-emotion message.
    ///     The emotion includes a label (e.g., "happy", "sad", "angry") and an intensity scale (1-3).
    ///     Typically use EventDeliveryPolicy.MainThread for animation or UI updates and
    ///     EventDeliveryPolicy.Immediate for lightweight logging or analytics.
    /// </remarks>
    public readonly struct CharacterEmotionChanged
    {
        /// <summary>
        ///     The character's unique identifier.
        /// </summary>
        public string CharacterId { get; }

        /// <summary>
        ///     The emotion label (e.g., "happy", "sad", "angry", "neutral", "surprised", "fearful", "disgusted").
        /// </summary>
        public string Emotion { get; }

        /// <summary>
        ///     The intensity/scale of the emotion (1-3, where 1 is subtle and 3 is intense).
        /// </summary>
        public int Intensity { get; }

        /// <summary>
        ///     When the emotion changed (UTC).
        /// </summary>
        public DateTime Timestamp { get; }
        public long Sequence { get; }
        public string UtteranceId { get; }
        public float Confidence { get; }
        public int DurationMilliseconds { get; }

        /// <summary>
        ///     Creates a new CharacterEmotionChanged event.
        /// </summary>
        public CharacterEmotionChanged(
            string characterId,
            string emotion,
            int intensity,
            DateTime timestamp)
        {
            CharacterId = characterId;
            Emotion = emotion;
            Intensity = Math.Clamp(intensity, 1, 3);
            Timestamp = timestamp;
            Sequence = -1;
            UtteranceId = string.Empty;
            Confidence = 1f;
            DurationMilliseconds = 0;
        }

        public CharacterEmotionChanged(string characterId, string emotion, int intensity, DateTime timestamp,
            long sequence, string utteranceId, float confidence, int durationMilliseconds)
        {
            CharacterId = characterId;
            Emotion = emotion;
            Intensity = Math.Clamp(intensity, 1, 3);
            Timestamp = timestamp;
            Sequence = sequence;
            UtteranceId = utteranceId ?? string.Empty;
            Confidence = float.IsNaN(confidence) ? 1f : Math.Clamp(confidence, 0f, 1f);
            DurationMilliseconds = Math.Max(0, durationMilliseconds);
        }

        /// <summary>
        ///     Creates a CharacterEmotionChanged event with the current UTC timestamp.
        /// </summary>
        /// <param name="characterId">The character's unique identifier</param>
        /// <param name="emotion">The emotion label</param>
        /// <param name="intensity">The intensity scale (1-3)</param>
        /// <returns>A new CharacterEmotionChanged event</returns>
        public static CharacterEmotionChanged Create(
            string characterId,
            string emotion,
            int intensity = 2)
        {
            return new CharacterEmotionChanged(
                characterId,
                emotion,
                intensity,
                DateTime.UtcNow
            );
        }

        /// <summary>
        ///     Checks if this is a neutral/default emotion state.
        /// </summary>
        public bool IsNeutral => string.Equals(Emotion, "neutral", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        ///     Checks if this is a high-intensity emotion (scale 3).
        /// </summary>
        public bool IsHighIntensity => Intensity >= 3;

        /// <summary>
        ///     Checks if this is a low-intensity/subtle emotion (scale 1).
        /// </summary>
        public bool IsLowIntensity => Intensity <= 1;

        /// <summary>
        ///     Gets a normalized intensity in <c>(0, 1]</c> using the same mapping the emotion
        ///     pipeline applies (<c>Intensity / 3</c>): scale 1 -> 0.33, 2 -> 0.67, 3 -> 1.0.
        ///     A subtle (scale 1) signal is still a real emotion, so it maps to 0.33 rather than 0.
        ///     This is the single source of truth for base intensity normalization; the emotion
        ///     controller adds its profile offset on top of this value.
        /// </summary>
        public float NormalizedIntensity => Intensity / 3f;
    }
}
