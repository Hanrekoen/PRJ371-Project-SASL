#nullable enable
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Convai.RestAPI.Internal.Models
{
    [Serializable]
    public class CreateSectionResponse
    {
        [JsonProperty("section_id")]
        public string SectionId { get; set; } = string.Empty;
    }

    [Serializable]
    public class EditSectionResponse
    {
        [JsonProperty("status")]
        public string? Status { get; set; }

        [JsonProperty("section_id")]
        public string? SectionId { get; set; }

        [JsonProperty("updated_data")]
        public JObject? UpdatedData { get; set; }
    }

    [Serializable]
    public class StatusResponse
    {
        [JsonProperty("STATUS")]
        public string? Status { get; set; }

        [JsonProperty("status")]
        public string? LowerStatus { get; set; }

        [JsonProperty("message")]
        public string? Message { get; set; }

        public bool WasSuccessful => string.Equals(Status, "Successful", StringComparison.OrdinalIgnoreCase)
                                     || string.Equals(LowerStatus, "success", StringComparison.OrdinalIgnoreCase);
    }
}

