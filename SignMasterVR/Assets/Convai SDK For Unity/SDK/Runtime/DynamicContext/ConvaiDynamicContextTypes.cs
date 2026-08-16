using System.Collections.Generic;
using Convai.Shared.Actions;

namespace Convai.Runtime.DynamicContext
{
    /// <summary>
    ///     Controls how dynamic context text is applied on the backend.
    /// </summary>
    public enum ConvaiContextUpdateMode
    {
        Append = 0,
        Replace,
        Reset
    }

    /// <summary>
    ///     Advanced typed request used to send raw dynamic context updates without exposing transport strings.
    /// </summary>
    public sealed class ConvaiDynamicContextUpdate
    {
        public ConvaiDynamicContextUpdate(
            string text,
            ConvaiContextUpdateMode mode = ConvaiContextUpdateMode.Append,
            ConvaiRespondMode reaction = ConvaiRespondMode.Auto,
            bool removeStatic = false,
            object currentAttentionObject = null,
            string updateId = null,
            ConvaiActionConfigPatch actionConfig = null)
        {
            Text = text;
            Mode = mode;
            Reaction = reaction;
            RemoveStatic = removeStatic;
            CurrentAttentionObject = currentAttentionObject;
            UpdateId = updateId;
            ActionConfig = actionConfig?.Clone();
        }

        public string Text { get; }
        public ConvaiContextUpdateMode Mode { get; }
        public ConvaiRespondMode Reaction { get; }
        public bool RemoveStatic { get; }
        public object CurrentAttentionObject { get; }
        public string UpdateId { get; }
        public ConvaiActionConfigPatch ActionConfig { get; }

        internal ConvaiDynamicContextUpdate WithRuntimeActionState(
            object currentAttentionObject,
            string updateId,
            ConvaiActionConfigPatch actionConfig) =>
            new(
                Text,
                Mode,
                Reaction,
                RemoveStatic,
                currentAttentionObject,
                updateId,
                actionConfig);
    }

    /// <summary>
    ///     Character-owned runtime context surface for authoring tracked state, chronological events,
    ///     and attention-object updates. Tracked calls update local state first, then batch one
    ///     backend <c>context-update</c> until <see cref="Flush" />, <c>CharacterReady</c>, reconnect
    ///     resync, or the normal batch window sends it.
    /// </summary>
    public interface IConvaiDynamicContext
    {
        /// <summary>
        ///     Sets or updates one tracked state entry in the local canonical store. If several
        ///     changes are staged before the batch sends, the strongest requested reaction wins for
        ///     the whole batch (<c>MustRespond</c> &gt; <c>Auto</c> &gt; <c>Silent</c>).
        /// </summary>
        public void SetState(string name, string value,
            ConvaiRespondMode reaction = ConvaiRespondMode.Silent);

        /// <summary>
        ///     Sets or updates multiple tracked state entries in one staged batch. Invalid entries
        ///     are skipped; changed entries are included in the next canonical text replacement.
        /// </summary>
        public void SetStates(IReadOnlyDictionary<string, string> states,
            ConvaiRespondMode reaction = ConvaiRespondMode.Silent);

        /// <summary>
        ///     Appends a chronological event entry to the tracked canonical text. Duplicate event
        ///     text inside one pending batch is deduped before the backend update is sent.
        /// </summary>
        public void AddEvent(string text, ConvaiRespondMode reaction = ConvaiRespondMode.Auto);

        /// <summary>
        ///     Removes one tracked state entry from local canonical state and stages a silent backend
        ///     replacement for the next batch send.
        /// </summary>
        public void RemoveState(string name);

        /// <summary>
        ///     Clears all tracked local dynamic context and stages a backend reset. When
        ///     <paramref name="removeStatic" /> is true, the reset also asks the backend to remove
        ///     static initial dynamic context for this session.
        /// </summary>
        public void Reset(bool removeStatic = false);

        /// <summary>
        ///     Stages the current object used by backend action-reference grounding. String values
        ///     must match active action-config object names; valid updates join the same reaction
        ///     escalation rules as text changes.
        /// </summary>
        public void SetCurrentAttentionObject(object currentAttentionObject,
            ConvaiRespondMode reaction = ConvaiRespondMode.Silent);

        /// <summary>
        ///     Stages a clear for the current backend attention object. The clear is sent in the next
        ///     dynamic-context batch or immediately when <see cref="Flush" /> is called.
        /// </summary>
        public void ClearCurrentAttentionObject(ConvaiRespondMode reaction = ConvaiRespondMode.Silent);

        /// <summary>
        ///     Sends any staged dynamic context and scene-metadata changes immediately when the
        ///     character is in conversation. If no transport is ready, staged data remains pending.
        /// </summary>
        public void Flush();

        /// <summary>
        ///     Reads the latest local tracked state value only. This does not query the backend and
        ///     does not include raw <see cref="Apply" /> updates.
        /// </summary>
        public bool TryGetStateValue(string name, out string value);

        /// <summary>
        ///     Sends a raw typed update without mutating tracked local state or pending batches.
        ///     Invalid updates and calls made while the character is not in conversation are dropped
        ///     with a warning.
        /// </summary>
        public void Apply(ConvaiDynamicContextUpdate update);
    }
}
