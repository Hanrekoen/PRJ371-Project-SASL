#nullable enable

using Newtonsoft.Json;

namespace Convai.Infrastructure.Protocol.Messages
{
    /// <summary>Payload for the llm-no-response server message.</summary>
    public sealed class LlmNoResponsePayload
    {
        /// <summary>Optional reason for the no-response decision (e.g., "abstain").</summary>
        [JsonProperty("reason")]
        public string? Reason { get; set; }
    }
}
