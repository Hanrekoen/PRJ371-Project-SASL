#nullable enable

using Newtonsoft.Json;

namespace Convai.Infrastructure.Protocol.Messages
{
    /// <summary>
    ///     Payload for a final user transcription message with multi-user speaker attribution.
    ///     Maps to backend's RTVIFinalTranscriptionMessage.
    /// </summary>
    public class FinalUserTranscriptionPayload
    {
        /// <summary>Gets or sets the final transcribed text.</summary>
        [JsonProperty("text")]
        public string? Text { get; set; }

        /// <summary>Gets or sets the stable message identifier for typed-text echo reconciliation.</summary>
        [JsonProperty("message_id")]
        public string? MessageId { get; set; }

        /// <summary>Gets or sets the unique speaker identifier from the backend's speaker directory.</summary>
        [JsonProperty("speaker_id")]
        public string? SpeakerId { get; set; }

        /// <summary>Gets or sets the human-readable speaker name for display.</summary>
        [JsonProperty("speaker_name")]
        public string? SpeakerName { get; set; }

        /// <summary>Gets or sets the LiveKit participant ID (SID) for multi-user attribution.</summary>
        [JsonProperty("participant_id")]
        public string? ParticipantId { get; set; }
    }
}
