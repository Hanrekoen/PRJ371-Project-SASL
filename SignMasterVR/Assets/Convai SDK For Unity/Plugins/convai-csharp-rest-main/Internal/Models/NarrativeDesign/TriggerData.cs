#nullable enable
using System;
using Newtonsoft.Json;
using UnityEngine;

namespace Convai.RestAPI.Internal.Models
{
    /// <summary>
    /// Represents a narrative design trigger with its associated data.
    /// Unity-serializable with explicit backing fields.
    /// </summary>
    [Serializable]
    public class TriggerData
    {
        [SerializeField] private string _triggerId = string.Empty;
        [SerializeField] private string _triggerName = string.Empty;
        [SerializeField] private string _triggerMessage = string.Empty;
        [SerializeField] private string _destinationSection = string.Empty;
        [SerializeField] private string _characterId = string.Empty;

        /// <summary>
        /// Default constructor for Unity serialization.
        /// </summary>
        public TriggerData()
        {
        }

        /// <summary>
        /// Initializes a new instance of the TriggerData class.
        /// </summary>
        [JsonConstructor]
        public TriggerData(string id, string name, string message, string destinationSectionID, string characterId, object? nodePosition = null)
        {
            _triggerId = id ?? string.Empty;
            _triggerName = name ?? string.Empty;
            _triggerMessage = message ?? string.Empty;
            _destinationSection = destinationSectionID ?? string.Empty;
            _characterId = characterId ?? string.Empty;
            NodePosition = nodePosition;
        }

        /// <summary>
        /// The unique identifier for the trigger.
        /// </summary>
        [JsonProperty("trigger_id")]
        public string TriggerId
        {
            get => _triggerId;
            internal set => _triggerId = value ?? string.Empty;
        }

        /// <summary>
        /// The name of the trigger.
        /// </summary>
        [JsonProperty("trigger_name")]
        public string TriggerName
        {
            get => _triggerName;
            internal set => _triggerName = value ?? string.Empty;
        }

        /// <summary>
        /// The message associated with this trigger.
        /// </summary>
        [JsonProperty("trigger_message")]
        public string TriggerMessage
        {
            get => _triggerMessage;
            internal set => _triggerMessage = value ?? string.Empty;
        }

        /// <summary>
        /// The ID of the section this trigger leads to.
        /// </summary>
        [JsonProperty("destination_section")]
        public string DestinationSection
        {
            get => _destinationSection;
            internal set => _destinationSection = value ?? string.Empty;
        }

        /// <summary>
        /// The ID of the character associated with this trigger.
        /// </summary>
        [JsonProperty("character_id")]
        public string CharacterId
        {
            get => _characterId;
            internal set => _characterId = value ?? string.Empty;
        }

        /// <summary>
        /// The saved node position for layout purposes.
        /// Not serialized by Unity (object type not supported).
        /// </summary>
        [JsonProperty("node_position")]
        [field: NonSerialized]
        public object? NodePosition { get; internal set; }
    }
}
