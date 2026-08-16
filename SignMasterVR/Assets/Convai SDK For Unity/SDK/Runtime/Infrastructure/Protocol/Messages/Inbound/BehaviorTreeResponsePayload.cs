#nullable enable

using Newtonsoft.Json;

namespace Convai.Infrastructure.Protocol.Messages
{
    /// <summary>Payload containing a behavior tree response.</summary>
    public class BehaviorTreeResponsePayload
    {
        /// <summary>Gets or sets the behavior tree code.</summary>
        [JsonProperty("bt_code")]
        public string? BtCode { get; set; }

        /// <summary>Gets or sets the behavior tree constants.</summary>
        [JsonProperty("bt_constants")]
        public string? BtConstants { get; set; }

        /// <summary>Gets or sets the narrative section identifier associated with the response.</summary>
        [JsonProperty("narrative_section_id")]
        public string? NarrativeSectionId { get; set; }
    }
}
