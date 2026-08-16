using Convai.Runtime;
using Convai.Runtime.DynamicContext;
using Convai.Shared.Actions;
using Newtonsoft.Json;

namespace Convai.Infrastructure.Protocol.Messages
{
    /// <summary>
    ///     Payload for updating the bot's ephemeral (temporary runtime) context.
    /// </summary>
    public class DynamicContext
    {
        public DynamicContext() { }

        public DynamicContext(ConvaiDynamicContextUpdate update)
        {
            Text = update?.Text;
            Mode = ResolveMode(update);
            RunLlm = update?.Reaction switch
            {
                ConvaiRespondMode.MustRespond => "true",
                ConvaiRespondMode.Silent => "false",
                _ => "auto"
            };
            RemoveStatic = update?.RemoveStatic == true;
            CurrentAttentionObject = update?.CurrentAttentionObject;
            UpdateId = update?.UpdateId;
            ActionConfig = update?.ActionConfig?.Clone();
        }

        /// <summary>New context text to apply. Required when <see cref="Mode" /> is not "reset".</summary>
        [JsonProperty("text", NullValueHandling = NullValueHandling.Ignore)]
        public string Text { get; set; }

        /// <summary>How to apply the context: "append", "replace", or "reset". Default "append".</summary>
        [JsonProperty("mode", NullValueHandling = NullValueHandling.Ignore)]
        public string Mode { get; set; }

        /// <summary>Whether to trigger an LLM response: "true", "false", or "auto". Default "auto".</summary>
        [JsonProperty("run_llm")]
        public string RunLlm { get; set; }

        /// <summary>Whether reset should also remove static connect-time context.</summary>
        [JsonProperty("remove_static", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool RemoveStatic { get; set; }

        /// <summary>
        ///     Optional action-config object name used for action-reference grounding.
        ///     Send empty string to clear the current attention object.
        /// </summary>
        [JsonProperty("current_attention_object", NullValueHandling = NullValueHandling.Ignore)]
        public object CurrentAttentionObject { get; set; }

        /// <summary>Optional client-provided update identifier for backend dedupe/retry handling.</summary>
        [JsonProperty("update_id", NullValueHandling = NullValueHandling.Ignore)]
        public string UpdateId { get; set; }

        /// <summary>Optional runtime action-config patch for the active session.</summary>
        [JsonProperty("action_config", NullValueHandling = NullValueHandling.Ignore)]
        public ConvaiActionConfigPatch ActionConfig { get; set; }

        private static string ResolveMode(ConvaiDynamicContextUpdate update)
        {
            if (update == null)
                return "append";

            if (update.Mode == ConvaiContextUpdateMode.Reset)
                return "reset";

            // Action-config-only updates omit mode so the backend treats them as affordance patches.
            if (update.Text == null && update.ActionConfig != null)
                return null;

            return update.Mode switch
            {
                ConvaiContextUpdateMode.Replace => "replace",
                _ => "append"
            };
        }
    }
}
