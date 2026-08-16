using System;
using Newtonsoft.Json.Linq;

namespace Convai.Domain.DomainEvents.Runtime
{
    /// <summary>
    ///     Domain event raised when the backend acknowledges a dynamic context update.
    /// </summary>
    public readonly struct DynamicContextUpdateResultReceived
    {
        private DynamicContextUpdateResultReceived(
            string status,
            string message,
            string updateId,
            int contextRevision,
            int tokenCount,
            int staticTokenCount,
            int runtimeTokenCount,
            int remainingTokens,
            string requestedRunLlm,
            string actualRunLlm,
            string downgradeReason,
            bool interrupted,
            bool llmTriggered,
            bool promptRebuild,
            string promptRebuildStatus,
            bool? actionConfigUpdated,
            bool? actionConfigCreated,
            int? actionsCount,
            int? objectsCount,
            int? charactersCount,
            string currentAttentionObject,
            bool? currentAttentionObjectCleared,
            bool? actionGenerationStrategyChanged,
            string actionGenerationStrategyStatus,
            JObject rawExtras,
            DateTime timestamp)
        {
            Status = status ?? string.Empty;
            Message = message ?? string.Empty;
            UpdateId = updateId ?? string.Empty;
            ContextRevision = contextRevision;
            TokenCount = tokenCount;
            StaticTokenCount = staticTokenCount;
            RuntimeTokenCount = runtimeTokenCount;
            RemainingTokens = remainingTokens;
            RequestedRunLlm = requestedRunLlm ?? string.Empty;
            ActualRunLlm = actualRunLlm ?? string.Empty;
            DowngradeReason = downgradeReason ?? string.Empty;
            Interrupted = interrupted;
            LlmTriggered = llmTriggered;
            PromptRebuild = promptRebuild;
            PromptRebuildStatus = promptRebuildStatus;
            ActionConfigUpdated = actionConfigUpdated;
            ActionConfigCreated = actionConfigCreated;
            ActionsCount = actionsCount;
            ObjectsCount = objectsCount;
            CharactersCount = charactersCount;
            CurrentAttentionObject = currentAttentionObject;
            CurrentAttentionObjectCleared = currentAttentionObjectCleared;
            ActionGenerationStrategyChanged = actionGenerationStrategyChanged;
            ActionGenerationStrategyStatus = actionGenerationStrategyStatus;
            RawExtras = rawExtras;
            Timestamp = timestamp;
        }

        public string Status { get; }
        public string Message { get; }
        public string UpdateId { get; }
        public int ContextRevision { get; }
        public int TokenCount { get; }
        public int StaticTokenCount { get; }
        public int RuntimeTokenCount { get; }
        public int RemainingTokens { get; }
        public string RequestedRunLlm { get; }
        public string ActualRunLlm { get; }
        public string DowngradeReason { get; }
        public bool Interrupted { get; }
        public bool LlmTriggered { get; }
        public bool PromptRebuild { get; }
        public string PromptRebuildStatus { get; }
        public bool? ActionConfigUpdated { get; }
        public bool? ActionConfigCreated { get; }
        public int? ActionsCount { get; }
        public int? ObjectsCount { get; }
        public int? CharactersCount { get; }
        public string CurrentAttentionObject { get; }
        public bool? CurrentAttentionObjectCleared { get; }
        public bool? ActionGenerationStrategyChanged { get; }
        public string ActionGenerationStrategyStatus { get; }
        public JObject RawExtras { get; }
        public DateTime Timestamp { get; }

        public static DynamicContextUpdateResultReceived Create(
            string status,
            string message,
            JObject extras)
        {
            extras ??= new JObject();
            string promptRebuildStatus = ExtrasReader.ReadOptionalString(extras, "prompt_rebuild");
            return new DynamicContextUpdateResultReceived(
                status,
                message,
                ExtrasReader.ReadString(extras, "update_id"),
                ExtrasReader.ReadInt(extras, "context_revision", "revision"),
                ExtrasReader.ReadInt(extras, "token_count", "word_count"),
                ExtrasReader.ReadInt(extras, "static_token_count"),
                ExtrasReader.ReadInt(extras, "runtime_token_count"),
                ExtrasReader.ReadInt(extras, "remaining_tokens", "remaining_words"),
                ExtrasReader.ReadString(extras, "requested_run_llm"),
                ExtrasReader.ReadString(extras, "actual_run_llm"),
                ExtrasReader.ReadString(extras, "downgrade_reason"),
                ExtrasReader.ReadBool(extras, "interrupted"),
                ExtrasReader.ReadBool(extras, "llm_triggered"),
                ExtrasReader.ReadBool(extras, "prompt_rebuild"),
                promptRebuildStatus,
                ExtrasReader.ReadNullableBool(extras, "action_config_updated"),
                ExtrasReader.ReadNullableBool(extras, "action_config_created"),
                ExtrasReader.ReadNullableInt(extras, "actions_count"),
                ExtrasReader.ReadNullableInt(extras, "objects_count"),
                ExtrasReader.ReadNullableInt(extras, "characters_count"),
                ExtrasReader.ReadOptionalString(extras, "current_attention_object"),
                ExtrasReader.ReadNullableBool(extras, "current_attention_object_cleared"),
                ExtrasReader.ReadNullableBool(extras, "action_generation_strategy_changed"),
                ExtrasReader.ReadOptionalString(extras, "action_generation_strategy_status"),
                extras,
                DateTime.UtcNow);
        }

    }
}
