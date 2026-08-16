#nullable enable

using Newtonsoft.Json;

namespace Convai.Infrastructure.Protocol.Messages
{
    /// <summary>Payload for bot transcription events emitted by the server.</summary>
    public class BotTranscriptionPayload
    {
        /// <summary>Gets or sets the transcribed bot text.</summary>
        [JsonProperty("text")]
        public string? Text { get; set; }

        /// <summary>Gets or sets the backend response identifier, when present.</summary>
        [JsonProperty("response_id")]
        public string? ResponseId { get; set; }

        /// <summary>Gets or sets the stable message identifier, when present.</summary>
        [JsonProperty("message_id")]
        public string? MessageId { get; set; }

        /// <summary>Gets or sets the backend turn identifier, when present.</summary>
        [JsonProperty("turn_id")]
        public string? TurnId { get; set; }

        /// <summary>Whether this output is intended to be spoken.</summary>
        [JsonProperty("spoken")]
        public bool? Spoken { get; set; }

        /// <summary>Backend aggregation mode used to produce this output.</summary>
        [JsonProperty("aggregated_by")]
        public string? AggregatedBy { get; set; }
    }
}
