#nullable enable

using System.Collections.Generic;
using Newtonsoft.Json;

namespace Convai.Infrastructure.Protocol.Messages
{
    /// <summary>Payload for the visemes server message.</summary>
    public sealed class VisemesPayload
    {
        /// <summary>Maps viseme keys to blend weights.</summary>
        [JsonProperty("visemes")]
        public Dictionary<string, float>? Visemes { get; set; }
    }
}
