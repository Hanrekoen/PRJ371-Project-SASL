#nullable enable

using Newtonsoft.Json;

namespace Convai.Infrastructure.Protocol.Messages
{
    /// <summary>Payload for the user-idle-warning server message.</summary>
    public sealed class UserIdleWarningPayload
    {
        /// <summary>Seconds remaining before the user is disconnected for inactivity.</summary>
        [JsonProperty("remaining_seconds")]
        public int RemainingSeconds { get; set; }

        /// <summary>Optional human-readable warning message.</summary>
        [JsonProperty("message")]
        public string? Message { get; set; }
    }
}
