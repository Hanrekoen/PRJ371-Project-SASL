#nullable enable

using Newtonsoft.Json;

namespace Convai.Infrastructure.Protocol.Messages
{
    /// <summary>Payload for bot turn completion events emitted by the server.</summary>
    public class BotTurnCompletedPayload
    {
        /// <summary>Gets or sets a value indicating whether the turn ended via interruption.</summary>
        [JsonProperty("was_interrupted")]
        public bool WasInterrupted { get; set; }
    }
}
