#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Convai.RestAPI.Internal.Models;
using Convai.RestAPI.Transport;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Convai.RestAPI.Services
{
    /// <summary>
    /// Service for narrative design API operations.
    /// </summary>
    public sealed class NarrativeService : ConvaiServiceBase
    {
        private const string ListSectionsEndpoint = "character/narrative/list-sections";
        private const string CreateSectionEndpoint = "character/narrative/create-section";
        private const string GetSectionEndpoint = "character/narrative/get-section";
        private const string EditSectionEndpoint = "character/narrative/edit-section";
        private const string DeleteSectionEndpoint = "character/narrative/delete-section";
        private const string AddDecisionEndpoint = "character/narrative/add-decision";
        private const string EditDecisionEndpoint = "character/narrative/edit-decision";
        private const string DeleteDecisionEndpoint = "character/narrative/delete-decision";
        private const string UpdateStartSectionEndpoint = "character/narrative/update-start-section-id";
        private const string UpdateNodePositionEndpoint = "character/narrative/update-node-position";
        private const string GetCurrentSectionEndpoint = "character/narrative/get-current-section";
        private const string ListTriggersEndpoint = "character/narrative/list-triggers";
        private const string CreateTriggerEndpoint = "character/narrative/create-trigger";
        private const string GetTriggerEndpoint = "character/narrative/get-trigger";
        private const string UpdateTriggerEndpoint = "character/narrative/update-trigger";
        private const string DeleteTriggerEndpoint = "character/narrative/delete-trigger";
        private const string ToggleNarrativeDrivenEndpoint = "character/toggle-is-narrative-driven";

        internal NarrativeService(ConvaiRestClientOptions options, IConvaiHttpTransport transport)
            : base(options, transport)
        {
        }

        #region Sections

        /// <summary>
        /// Gets all sections for a character.
        /// </summary>
        public async Task<IReadOnlyList<SectionData>> GetSectionsAsync(
            string characterId,
            CancellationToken cancellationToken = default)
        {
            var requestBody = new { character_id = characterId };
            return await PostAsync<List<SectionData>>(
                ListSectionsEndpoint,
                requestBody,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Gets a specific section.
        /// </summary>
        public async Task<SectionData?> GetSectionAsync(
            string characterId,
            string sectionId,
            CancellationToken cancellationToken = default)
        {
            var requestBody = new { character_id = characterId, section_id = sectionId };
            return await PostAsync<SectionData>(
                GetSectionEndpoint,
                requestBody,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Creates a new section.
        /// </summary>
        public async Task<CreateSectionResponse> CreateSectionAsync(
            string characterId,
            string sectionName,
            string objective,
            JObject? updatedCharacterData = null,
            string? behaviorTreeCode = null,
            string? btConstants = null,
            IList<float>? nodePosition = null,
            CancellationToken cancellationToken = default)
        {
            var requestBody = new Dictionary<string, object?>
            {
                { "character_id", characterId },
                { "section_name", sectionName },
                { "objective", objective }
            };

            if (updatedCharacterData != null)
                requestBody["updated_character_data"] = updatedCharacterData;
            if (behaviorTreeCode != null)
                requestBody["behavior_tree_code"] = behaviorTreeCode;
            if (btConstants != null)
                requestBody["bt_constants"] = btConstants;
            if (nodePosition != null)
                requestBody["node_position"] = nodePosition;

            return await PostAsync<CreateSectionResponse>(
                CreateSectionEndpoint,
                requestBody,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Edits an existing section.
        /// </summary>
        public async Task<EditSectionResponse> EditSectionAsync(
            string characterId,
            string sectionId,
            NarrativeSectionUpdateData updateData,
            CancellationToken cancellationToken = default)
        {
            var requestBody = new
            {
                character_id = characterId,
                section_id = sectionId,
                updated_data = updateData
            };

            return await PostAsync<EditSectionResponse>(
                EditSectionEndpoint,
                requestBody,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Deletes a section.
        /// </summary>
        public async Task<StatusResponse> DeleteSectionAsync(
            string characterId,
            string sectionId,
            CancellationToken cancellationToken = default)
        {
            var requestBody = new { character_id = characterId, section_id = sectionId };
            return await PostAsync<StatusResponse>(
                DeleteSectionEndpoint,
                requestBody,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        #endregion

        #region Decisions

        /// <summary>
        /// Adds a decision to a section.
        /// </summary>
        public async Task<StatusResponse> AddDecisionAsync(
            string characterId,
            string fromSectionId,
            string toSectionId,
            string criteria,
            int? priority = null,
            CancellationToken cancellationToken = default)
        {
            var requestBody = new Dictionary<string, object?>
            {
                { "character_id", characterId },
                { "from_section_id", fromSectionId },
                { "to_section_id", toSectionId },
                { "criteria", criteria }
            };

            if (priority.HasValue)
                requestBody["priority"] = priority.Value;

            return await PostAsync<StatusResponse>(
                AddDecisionEndpoint,
                requestBody,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Edits a decision.
        /// </summary>
        public async Task<StatusResponse> EditDecisionAsync(
            string characterId,
            string fromSectionId,
            string toSectionId,
            string criteria,
            NarrativeDecisionUpdatePayload updateData,
            CancellationToken cancellationToken = default)
        {
            var requestBody = new
            {
                character_id = characterId,
                from_section_id = fromSectionId,
                to_section_id = toSectionId,
                criteria = criteria,
                updated_data = updateData
            };

            return await PostAsync<StatusResponse>(
                EditDecisionEndpoint,
                requestBody,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Deletes a decision.
        /// </summary>
        public async Task<StatusResponse> DeleteDecisionAsync(
            string characterId,
            string fromSectionId,
            string toSectionId,
            string criteria,
            CancellationToken cancellationToken = default)
        {
            var requestBody = new
            {
                character_id = characterId,
                from_section_id = fromSectionId,
                to_section_id = toSectionId,
                criteria = criteria
            };

            return await PostAsync<StatusResponse>(
                DeleteDecisionEndpoint,
                requestBody,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        #endregion

        #region Triggers

        /// <summary>
        /// Gets all triggers for a character.
        /// </summary>
        public async Task<IReadOnlyList<TriggerData>> GetTriggersAsync(
            string characterId,
            CancellationToken cancellationToken = default)
        {
            var requestBody = new { character_id = characterId };
            return await PostAsync<List<TriggerData>>(
                ListTriggersEndpoint,
                requestBody,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Gets a specific trigger.
        /// </summary>
        public async Task<TriggerData?> GetTriggerAsync(
            string characterId,
            string triggerId,
            CancellationToken cancellationToken = default)
        {
            var requestBody = new { character_id = characterId, trigger_id = triggerId };
            return await PostAsync<TriggerData>(
                GetTriggerEndpoint,
                requestBody,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Creates a new trigger.
        /// </summary>
        public async Task<TriggerData> CreateTriggerAsync(
            string characterId,
            string triggerName,
            string triggerMessage,
            string? destinationSection = null,
            IList<float>? nodePosition = null,
            CancellationToken cancellationToken = default)
        {
            var requestBody = new Dictionary<string, object?>
            {
                { "character_id", characterId },
                { "trigger_name", triggerName },
                { "trigger_message", triggerMessage }
            };

            if (destinationSection != null)
                requestBody["destination_section"] = destinationSection;
            if (nodePosition != null)
                requestBody["node_position"] = nodePosition;

            return await PostAsync<TriggerData>(
                CreateTriggerEndpoint,
                requestBody,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Updates a trigger.
        /// </summary>
        public async Task<StatusResponse> UpdateTriggerAsync(
            string characterId,
            string triggerId,
            NarrativeTriggerUpdateData updateData,
            CancellationToken cancellationToken = default)
        {
            var requestBody = new
            {
                character_id = characterId,
                trigger_id = triggerId,
                updated_data = updateData
            };

            return await PostAsync<StatusResponse>(
                UpdateTriggerEndpoint,
                requestBody,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Deletes a trigger.
        /// </summary>
        public async Task<StatusResponse> DeleteTriggerAsync(
            string characterId,
            string triggerId,
            CancellationToken cancellationToken = default)
        {
            var requestBody = new { character_id = characterId, trigger_id = triggerId };
            return await PostAsync<StatusResponse>(
                DeleteTriggerEndpoint,
                requestBody,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        #endregion

        #region Other

        /// <summary>
        /// Toggles narrative-driven mode for a character.
        /// </summary>
        public async Task<StatusResponse> ToggleNarrativeDrivenAsync(
            string characterId,
            bool isNarrativeDriven,
            CancellationToken cancellationToken = default)
        {
            var requestBody = new
            {
                character_id = characterId,
                is_narrative_driven = isNarrativeDriven
            };

            return await PostAsync<StatusResponse>(
                ToggleNarrativeDrivenEndpoint,
                requestBody,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Updates the start section for a character.
        /// </summary>
        public async Task<StatusResponse> UpdateStartSectionAsync(
            string characterId,
            string? startSectionId,
            CancellationToken cancellationToken = default)
        {
            var requestBody = new Dictionary<string, object?>
            {
                { "character_id", characterId },
                { "start_narrative_section_id", startSectionId }
            };

            return await PostAsync<StatusResponse>(
                UpdateStartSectionEndpoint,
                requestBody,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Updates node positions in the editor.
        /// </summary>
        public async Task<StatusResponse> UpdateNodePositionsAsync(
            string nodeType,
            JArray updatedNodes,
            CancellationToken cancellationToken = default)
        {
            var requestBody = new
            {
                node_type = nodeType,
                updated_nodes = updatedNodes
            };

            return await PostAsync<StatusResponse>(
                UpdateNodePositionEndpoint,
                requestBody,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Gets the current narrative section for a session.
        /// </summary>
        public async Task<JObject?> GetCurrentSectionAsync(
            string characterId,
            string sessionId,
            CancellationToken cancellationToken = default)
        {
            var requestBody = new { character_id = characterId, session_id = sessionId };
            return await PostAsync<JObject>(
                GetCurrentSectionEndpoint,
                requestBody,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        #endregion
    }
}
