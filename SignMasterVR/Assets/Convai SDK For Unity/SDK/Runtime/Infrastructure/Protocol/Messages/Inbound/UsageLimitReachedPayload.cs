#nullable enable

using Newtonsoft.Json;

namespace Convai.Infrastructure.Protocol.Messages
{
    /// <summary>Payload for the usage-limit-reached server message.</summary>
    /// <remarks>
    ///     Sent by the backend when a usage quota (daily, monthly, etc.) is exhausted.
    ///     The pipeline is stopped immediately after this message.
    /// </remarks>
    public sealed class UsageLimitReachedPayload
    {
        /// <summary>The type of quota that was exceeded (e.g., "daily", "monthly", "additional").</summary>
        [JsonProperty("quota_type")]
        public string? QuotaType { get; set; }

        /// <summary>Human-readable description of the limit breach.</summary>
        [JsonProperty("message")]
        public string? Message { get; set; }
    }
}
