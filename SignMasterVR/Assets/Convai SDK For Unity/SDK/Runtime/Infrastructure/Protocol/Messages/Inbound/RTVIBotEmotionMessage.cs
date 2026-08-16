#nullable enable

using Newtonsoft.Json;

namespace Convai.Infrastructure.Protocol.Messages
{
    /// <summary>Inbound bot emotion payload deserialized from server-message with type "bot-emotion".</summary>
    public sealed class RTVIBotEmotionMessage
    {
        /// <summary>Gets or sets the emotion label.</summary>
        [JsonProperty("emotion")]
        public string? Emotion { get; set; }

        /// <summary>Gets or sets the emotion intensity scale (1-3).</summary>
        [JsonProperty("scale")]
        public int Scale { get; set; }

        /// <summary>Optional monotonic sequence number for stale/duplicate rejection.</summary>
        [JsonProperty("sequence")]
        public long? Sequence { get; set; }

        /// <summary>Optional utterance identifier used to correlate affect with speech.</summary>
        [JsonProperty("utterance_id")]
        public string? UtteranceId { get; set; }

        /// <summary>Optional detector confidence in [0, 1].</summary>
        [JsonProperty("confidence")]
        public float? Confidence { get; set; }

        /// <summary>Optional server capture time in Unix milliseconds.</summary>
        [JsonProperty("timestamp_ms")]
        public long? TimestampMilliseconds { get; set; }

        /// <summary>Optional intended expression duration in milliseconds.</summary>
        [JsonProperty("duration_ms")]
        public int? DurationMilliseconds { get; set; }
    }
}
