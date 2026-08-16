#nullable enable

using Newtonsoft.Json;

namespace Convai.Infrastructure.Protocol.Messages
{
    /// <summary>Payload for the moderation-response server message.</summary>
    /// <remarks>
    ///     Sent by the backend after content moderation evaluates user input.
    ///     When <see cref="Result" /> is <c>true</c>, the input was flagged and the
    ///     character response may be replaced with a refusal.
    /// </remarks>
    public sealed class ModerationResponsePayload
    {
        /// <summary>Whether the input was flagged by moderation (<c>true</c> = flagged).</summary>
        [JsonProperty("result")]
        public bool Result { get; set; }

        /// <summary>The user input that was evaluated.</summary>
        [JsonProperty("user_input")]
        public string? UserInput { get; set; }

        /// <summary>Optional reason for the moderation decision.</summary>
        [JsonProperty("reason")]
        public string? Reason { get; set; }
    }
}
