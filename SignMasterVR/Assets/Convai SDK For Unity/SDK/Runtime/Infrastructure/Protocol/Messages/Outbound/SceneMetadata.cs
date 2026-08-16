using System;
using Newtonsoft.Json;

namespace Convai.Infrastructure.Protocol.Messages
{
    /// <summary>Scene metadata entry.</summary>
    [Serializable]
    public class SceneMetadata
    {
        /// <summary>Gets or sets the metadata name.</summary>
        [JsonProperty("name")]
        public string Name { get; set; }

        /// <summary>Gets or sets the metadata description.</summary>
        [JsonProperty("description")]
        public string Description { get; set; }
    }
}
