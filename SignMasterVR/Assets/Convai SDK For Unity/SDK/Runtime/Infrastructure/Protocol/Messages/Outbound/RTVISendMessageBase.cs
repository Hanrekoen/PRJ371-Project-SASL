using System;
using Newtonsoft.Json;

namespace Convai.Infrastructure.Protocol.Messages
{
    /// <summary>
    ///     Base class for outbound RTVI (Real-Time Voice Inference) messages sent from the client to the server.
    ///     RTVI is the application-level messaging protocol layered on top of the LiveKit data channel.
    /// </summary>
    public abstract class RTVISendMessageBase
    {
        /// <summary>Gets the message label.</summary>
        [JsonProperty("label")]
        public string Label { get; protected set; } = "rtvi-ai";

        /// <summary>Gets the message type discriminator.</summary>
        [JsonProperty("type")]
        public string Type { get; protected set; }

        /// <summary>Gets the unique message identifier.</summary>
        [JsonProperty("id")]
        public string Id { get; protected set; } = Guid.NewGuid().ToString();

        /// <summary>Gets the message payload mapped to the <c>data</c> field.</summary>
        [JsonProperty("data")]
        public object Data { get; protected set; }
    }
}
