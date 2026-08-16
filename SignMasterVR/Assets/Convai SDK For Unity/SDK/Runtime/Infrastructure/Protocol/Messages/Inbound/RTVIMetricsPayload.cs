#nullable enable
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Convai.Infrastructure.Protocol.Messages
{
    /// <summary>
    ///     Inbound RTVI metrics payload (label "rtvi-ai", type "metrics").
    ///     Received on the WebRTC data channel when debug=true was set in the connect request.
    ///     Each message contains at most one of: ttfb, processing, or custom.
    /// </summary>
    public sealed class RTVIMetricsPayload
    {
        /// <summary>TTFB entries (seconds from first input to first output per processor).</summary>
        [JsonProperty("ttfb")]
        public JToken? Ttfb { get; set; }

        /// <summary>Processing time entries (seconds per processor for the turn).</summary>
        [JsonProperty("processing")]
        public JToken? Processing { get; set; }

        /// <summary>Custom metrics (e.g. NeuroSync: output_fps, blendshapes_received, etc.).</summary>
        [JsonProperty("custom")]
        public JToken? Custom { get; set; }
    }
}
