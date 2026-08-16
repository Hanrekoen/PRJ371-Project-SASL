#nullable enable
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Convai.RestAPI.Internal.Models
{
    /// <summary>
    /// Represents a decision that can be made within a narrative section.
    /// Decisions define criteria-based transitions to other sections.
    /// </summary>
    [Serializable]
    public class SectionDecision
    {
        /// <summary>
        /// The criteria that must be met for this decision to be selected.
        /// Use "*" for wildcard (always matches).
        /// </summary>
        [JsonProperty("criteria")]
        public string Criteria { get; set; } = string.Empty;

        /// <summary>
        /// The section ID to transition to when this decision is selected.
        /// </summary>
        [JsonProperty("next_section_id")]
        public string NextSectionId { get; set; } = string.Empty;

        /// <summary>
        /// Returns true if this decision is a wildcard (always matches).
        /// </summary>
        public bool IsWildcard => !string.IsNullOrEmpty(Criteria) && Criteria.Trim() == "*";
    }

    /// <summary>
    /// Represents a section-specific trigger override.
    /// Allows sections to override the default destination of a trigger.
    /// </summary>
    [Serializable]
    public class SectionTriggerOverride
    {
        /// <summary>
        /// The unique identifier of the trigger being overridden.
        /// </summary>
        [JsonProperty("trigger_id")]
        public string TriggerId { get; set; } = string.Empty;

        /// <summary>
        /// The section ID to transition to when this trigger is invoked from this section.
        /// </summary>
        [JsonProperty("destination_section")]
        public string DestinationSection { get; set; } = string.Empty;
    }

    /// <summary>
    /// Represents a narrative design section with its associated data.
    /// </summary>
    [Serializable]
    public class SectionData
    {
        /// <summary>
        /// Initializes a new instance of the SectionData class.
        /// </summary>
        [JsonConstructor]
        public SectionData(string sectionID, string sectionName, JToken? behaviorTreeConstants, string objective, string characterId, object decisions, object parents, object triggers, object updatedCharacterData, object? nodePosition = null, string? behaviorTreeCode = null)
        {
            SectionId = sectionID;
            SectionName = sectionName;
            BehaviorTreeConstants = behaviorTreeConstants;
            Objective = objective;
            CharacterId = characterId;
            Decisions = decisions;
            Parents = parents;
            Triggers = triggers;
            UpdatedCharacterData = updatedCharacterData;
            NodePosition = nodePosition;
            BehaviorTreeCode = behaviorTreeCode;
        }

        /// <summary>
        /// The unique identifier for the section.
        /// </summary>
        [JsonProperty("section_id")]
        public string SectionId { get; internal set; }

        /// <summary>
        /// The name of the section.
        /// </summary>
        [JsonProperty("section_name")]
        public string SectionName { get; internal set; }

        /// <summary>
        /// Constants used in the behavior tree for this section.
        /// </summary>
        [JsonProperty("bt_constants")]
        public JToken? BehaviorTreeConstants { get; internal set; }

        /// <summary>
        /// The objective or goal of this narrative section.
        /// </summary>
        [JsonProperty("objective")]
        public string Objective { get; internal set; }

        /// <summary>
        /// The ID of the character associated with this section.
        /// </summary>
        [JsonProperty("character_id")]
        public string CharacterId { get; internal set; }

        /// <summary>
        /// Decisions that can be made within this section (raw object).
        /// Use <see cref="GetDecisions"/> for strongly-typed access.
        /// </summary>
        [JsonProperty("decisions")]
        public object Decisions { get; internal set; }

        /// <summary>
        /// Parent sections that lead to this section.
        /// </summary>
        [JsonProperty("parents")]
        public object Parents { get; internal set; }

        /// <summary>
        /// Triggers that can be activated in this section (raw object).
        /// Use <see cref="GetTriggerOverrides"/> for strongly-typed access.
        /// </summary>
        [JsonProperty("triggers")]
        public object Triggers { get; internal set; }

        /// <summary>
        /// Updated character data after section completion.
        /// Use <see cref="GetUpdatedCharacterDataDict"/> for dictionary access.
        /// </summary>
        [JsonProperty("updated_character_data")]
        public object UpdatedCharacterData { get; internal set; }

        /// <summary>
        /// The stored node position for this section in the editor.
        /// </summary>
        [JsonProperty("node_position")]
        public object? NodePosition { get; internal set; }

        /// <summary>
        /// Serialized behavior tree code associated with this section.
        /// </summary>
        [JsonProperty("behavior_tree_code")]
        public string? BehaviorTreeCode { get; internal set; }

        /// <summary>
        /// Gets the decisions as a strongly-typed list.
        /// </summary>
        /// <returns>List of SectionDecision objects, or empty list if none.</returns>
        public List<SectionDecision> GetDecisions()
        {
            if (Decisions == null) return new List<SectionDecision>();

            try
            {
                if (Decisions is JArray jArray)
                {
                    return jArray.ToObject<List<SectionDecision>>() ?? new List<SectionDecision>();
                }

                string json = JsonConvert.SerializeObject(Decisions);
                return JsonConvert.DeserializeObject<List<SectionDecision>>(json) ?? new List<SectionDecision>();
            }
            catch
            {
                return new List<SectionDecision>();
            }
        }

        /// <summary>
        /// Gets the trigger overrides as a strongly-typed list.
        /// </summary>
        /// <returns>List of SectionTriggerOverride objects, or empty list if none.</returns>
        public List<SectionTriggerOverride> GetTriggerOverrides()
        {
            if (Triggers == null) return new List<SectionTriggerOverride>();

            try
            {
                if (Triggers is JArray jArray)
                {
                    return jArray.ToObject<List<SectionTriggerOverride>>() ?? new List<SectionTriggerOverride>();
                }

                string json = JsonConvert.SerializeObject(Triggers);
                return JsonConvert.DeserializeObject<List<SectionTriggerOverride>>(json) ?? new List<SectionTriggerOverride>();
            }
            catch
            {
                return new List<SectionTriggerOverride>();
            }
        }

        /// <summary>
        /// Gets the updated character data as a dictionary.
        /// </summary>
        /// <returns>Dictionary of character data updates, or empty dictionary if none.</returns>
        public Dictionary<string, object> GetUpdatedCharacterDataDict()
        {
            if (UpdatedCharacterData == null) return new Dictionary<string, object>();

            try
            {
                if (UpdatedCharacterData is JObject jObject)
                {
                    return jObject.ToObject<Dictionary<string, object>>() ?? new Dictionary<string, object>();
                }

                string json = JsonConvert.SerializeObject(UpdatedCharacterData);
                return JsonConvert.DeserializeObject<Dictionary<string, object>>(json) ?? new Dictionary<string, object>();
            }
            catch
            {
                return new Dictionary<string, object>();
            }
        }

        /// <summary>
        /// Gets the behavior tree constants as a dictionary.
        /// </summary>
        /// <returns>Dictionary of constants, or empty dictionary if none.</returns>
        public Dictionary<string, object> GetBehaviorTreeConstantsDict()
        {
            if (BehaviorTreeConstants == null) return new Dictionary<string, object>();

            try
            {
                if (BehaviorTreeConstants is JObject jObject)
                {
                    return jObject.ToObject<Dictionary<string, object>>() ?? new Dictionary<string, object>();
                }

                return new Dictionary<string, object>();
            }
            catch
            {
                return new Dictionary<string, object>();
            }
        }

        /// <summary>
        /// Checks if this section contains a wildcard decision (automatic transition).
        /// </summary>
        /// <returns>True if a wildcard decision exists.</returns>
        public bool HasWildcardDecision()
        {
            List<SectionDecision> decisions = GetDecisions();
            foreach (SectionDecision decision in decisions)
            {
                if (decision.IsWildcard) return true;
            }
            return false;
        }

        /// <summary>
        /// Gets the wildcard decision if one exists.
        /// </summary>
        /// <returns>The wildcard decision, or null if none.</returns>
        public SectionDecision? GetWildcardDecision()
        {
            List<SectionDecision> decisions = GetDecisions();
            foreach (SectionDecision decision in decisions)
            {
                if (decision.IsWildcard) return decision;
            }
            return null;
        }
    }
}
