#nullable enable

using Newtonsoft.Json;

namespace Convai.Infrastructure.Protocol.Messages
{
    /// <summary>Payload for the interaction-created server message.</summary>
    public sealed class InteractionCreatedPayload
    {
        /// <summary>Backend-generated interaction identifier.</summary>
        [JsonProperty("interaction_id")]
        public string? InteractionId { get; set; }

        /// <summary>Character session identifier associated with this interaction.</summary>
        [JsonProperty("character_session_id")]
        public string? CharacterSessionId { get; set; }
    }
}
