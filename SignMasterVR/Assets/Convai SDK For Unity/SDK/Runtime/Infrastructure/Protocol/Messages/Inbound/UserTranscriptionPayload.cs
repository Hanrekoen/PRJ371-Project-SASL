#nullable enable

using Newtonsoft.Json;

namespace Convai.Infrastructure.Protocol.Messages
{
    /// <summary>Payload for user transcription events emitted by the server.</summary>
    public class UserTranscriptionPayload
    {
        /// <summary>Gets or sets the participant identifier associated with the transcription.</summary>
        [JsonProperty("participant_id")]
        public string? ParticipantId { get; set; }

        /// <summary>Gets or sets the transcribed text.</summary>
        [JsonProperty("text")]
        public string? Text { get; set; }

        /// <summary>Gets or sets a value indicating whether this transcription segment is final.</summary>
        [JsonProperty("final")]
        public bool IsFinal { get; set; }
    }
}
