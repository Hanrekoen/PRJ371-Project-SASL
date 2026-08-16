using System;
using System.Collections;
using System.Collections.Generic;
using Convai.Domain.DomainEvents.Runtime;
using Convai.Runtime.Actions;
using Convai.Runtime.DynamicContext;
using Convai.Runtime.Room;
using Convai.Shared.Actions;
using UnityEngine;

namespace Convai.Runtime.Components
{
    public partial class ConvaiCharacter : IConvaiDynamicContext
    {
        /// <summary>Default dynamic-context batching delay before staged updates are sent.</summary>
        public const float DynamicContextBatchDelaySeconds = 0.5f;
        private const float DynamicContextMaxBatchDelaySeconds = 3f;
        private const float RuntimeActionUpdateAckTimeoutSeconds = 30f;
        private const float RuntimeActionUpdateAckPollSeconds = 1f;

        private readonly ConvaiDynamicContextTracker _dynamicContextTracker = new();
        private readonly List<PendingRuntimeActionStateUpdate> _pendingRuntimeActionStateUpdates = new();
        private Coroutine _dynamicContextFlushCoroutine;
        private Coroutine _runtimeActionUpdateAckMonitorCoroutine;
        private float _dynamicContextBatchWindowStartTime = -1f;
        private float _dynamicContextNextFlushTime = -1f;
        private long _dynamicContextUpdateSequence;

        private sealed class PendingRuntimeActionStateUpdate
        {
            public string UpdateId { get; set; }
            public ConvaiActionConfigPatch ActionConfig { get; set; }
            public string TopLevelAttentionObject { get; set; }
            public DateTime SentAtUtc { get; set; }
            public bool HasAcknowledgement { get; set; }
            public DateTime AcknowledgedAtUtc { get; set; }
            public DynamicContextUpdateResultReceived Acknowledgement { get; set; }
        }

        /// <summary>
        ///     Character-owned runtime surface for tracked dynamic context state and events.
        /// </summary>
        public IConvaiDynamicContext DynamicContext => this;

        private IConvaiDynamicContextTransport DynamicContextTransport => ConnectionService as IConvaiDynamicContextTransport;

        void IConvaiDynamicContext.SetState(
            string name,
            string value,
            ConvaiRespondMode reaction)
        {
            if (!TryValidateDynamicContextStateName(name) || !TryValidateDynamicContextStateValue(name, value)) return;

            if (_dynamicContextTracker.StageState(name, value, reaction))
                ScheduleDynamicContextFlush();
        }

        void IConvaiDynamicContext.SetStates(
            IReadOnlyDictionary<string, string> states,
            ConvaiRespondMode reaction)
        {
            if (states == null || states.Count == 0)
            {
                Logger?.Warning($"[{_characterName}] Cannot set empty dynamic context states");
                return;
            }

            bool changed = false;
            foreach (KeyValuePair<string, string> state in states)
            {
                if (!TryValidateDynamicContextStateName(state.Key) ||
                    !TryValidateDynamicContextStateValue(state.Key, state.Value))
                    continue;

                changed |= _dynamicContextTracker.StageState(state.Key, state.Value, reaction);
            }

            if (changed) ScheduleDynamicContextFlush();
        }

        void IConvaiDynamicContext.AddEvent(string text, ConvaiRespondMode reaction)
        {
            if (!TryValidateDynamicContextEventText(text)) return;

            if (_dynamicContextTracker.StageEvent(text, reaction))
                ScheduleDynamicContextFlush();
        }

        void IConvaiDynamicContext.RemoveState(string name)
        {
            if (!TryValidateDynamicContextStateName(name)) return;

            if (_dynamicContextTracker.StageStateRemoval(name))
                ScheduleDynamicContextFlush();
        }

        void IConvaiDynamicContext.Reset(bool removeStatic)
        {
            _dynamicContextTracker.StageReset(removeStatic);
            ResetDynamicContextBatchTiming();
            ScheduleDynamicContextFlush();
        }

        void IConvaiDynamicContext.SetCurrentAttentionObject(
            object currentAttentionObject,
            ConvaiRespondMode reaction) =>
            StageDynamicContextAttentionObject(currentAttentionObject, reaction);

        void IConvaiDynamicContext.ClearCurrentAttentionObject(ConvaiRespondMode reaction) =>
            StageDynamicContextAttentionObject(string.Empty, reaction);

        void IConvaiDynamicContext.Flush() => FlushPendingContextUpdates();

        bool IConvaiDynamicContext.TryGetStateValue(string name, out string value)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                value = null;
                return false;
            }

            return _dynamicContextTracker.TryGetStateValue(name, out value);
        }

        void IConvaiDynamicContext.Apply(ConvaiDynamicContextUpdate update)
        {
            if (!TryValidateRawDynamicContextUpdate(update)) return;

            if (!IsInConversation)
            {
                Logger?.Warning(
                    $"[{_characterName}] Cannot apply raw dynamic context update: not in conversation");
                return;
            }

            if (!TryPrepareRuntimeActionStateUpdate(
                    update,
                    out ConvaiDynamicContextUpdate preparedUpdate,
                    out ConvaiActionConfigReconciliation reconciliation,
                    out bool hasRuntimeActionState,
                    out string error))
            {
                Logger?.Warning(
                    $"[{_characterName}] Raw dynamic context update rejected (invalid_action_patch): {error}");
                return;
            }

            if (!TrySendDynamicContextUpdate(preparedUpdate, "raw dynamic context update"))
                return;

            if (hasRuntimeActionState)
                RegisterPendingRuntimeActionStateUpdate(preparedUpdate, reconciliation);
        }

        private void StageDynamicContextAttentionObject(
            object currentAttentionObject,
            ConvaiRespondMode reaction)
        {
            if (currentAttentionObject == null)
            {
                Logger?.Warning(
                    $"[{_characterName}] Dynamic context attention object cannot be null");
                return;
            }

            // Attention is validated against the predicted config here, at stage time. Send any
            // staged runtime target registrations first so a target registered moments ago is part
            // of that prediction — otherwise setting attention to a just-registered target would be
            // rejected purely because its own sync had not left the batch window yet.
            FlushPendingActionConfigSync();

            if (!TryBuildPredictedRuntimeActionConfig(out ConvaiActionConfig predictedConfig, out string error) ||
                !ConvaiActionConfigPatchReconciler.TryReconcile(
                    predictedConfig,
                    null,
                    currentAttentionObject,
                    GetRuntimeActionDefinitionCatalog(),
                    out ConvaiActionConfigReconciliation reconciliation,
                    out error))
            {
                Logger?.Warning(
                    $"[{_characterName}] Dynamic context attention update rejected (invalid_attention): {error}");
                return;
            }

            _dynamicContextTracker.StageAttention(reconciliation.TopLevelAttentionObject, reaction);
            ScheduleDynamicContextFlush();
        }

        private void FlushPendingDynamicContext()
        {
            if (!IsInConversation) return;

            StopScheduledDynamicContextFlush();

            if (_dynamicContextTracker.HasPendingReset)
            {
                if (TrySendDynamicContextUpdate(
                        new ConvaiDynamicContextUpdate(
                            null,
                            ConvaiContextUpdateMode.Reset,
                            ConvaiRespondMode.Silent,
                            removeStatic: _dynamicContextTracker.PendingRemoveStatic,
                            updateId: CreateDynamicContextUpdateId()),
                        "pending dynamic context reset"))
                {
                    _dynamicContextTracker.ClearPendingReset();
                    ResetDynamicContextBatchTiming();
                }

                return;
            }

            if (!_dynamicContextTracker.HasPendingBatch) return;

            ConvaiDynamicContextBatch batch = _dynamicContextTracker.BuildPendingBatch();
            var update = new ConvaiDynamicContextUpdate(
                batch.Text,
                batch.Mode,
                batch.Reaction,
                currentAttentionObject: batch.HasAttention ? batch.AttentionObject : null,
                updateId: CreateDynamicContextUpdateId());

            if (!TryPrepareRuntimeActionStateUpdate(
                    update,
                    out ConvaiDynamicContextUpdate preparedUpdate,
                    out ConvaiActionConfigReconciliation reconciliation,
                    out bool hasRuntimeActionState,
                    out string error))
            {
                Logger?.Warning(
                    $"[{_characterName}] Dynamic context batch rejected (invalid_action_patch): {error}");
                return;
            }

            if (TrySendDynamicContextUpdate(preparedUpdate, "dynamic context batch"))
            {
                if (hasRuntimeActionState)
                    RegisterPendingRuntimeActionStateUpdate(preparedUpdate, reconciliation);

                _dynamicContextTracker.ClearPendingBatch();
                ResetDynamicContextBatchTiming();
            }
        }

        private void FlushPendingContextUpdates()
        {
            // Registry sync first: it enters the pending runtime-action queue, so a same-flush
            // attention update reconciles against a predicted config that already contains the
            // freshly registered targets. Nothing is committed locally until each is acknowledged —
            // a discarded sync discards the attention update that depended on it.
            FlushPendingActionConfigSync();
            FlushPendingDynamicContext();
            FlushPendingSceneMetadata();
        }

        private void ScheduleDynamicContextFlush()
        {
            if (!IsInConversation || !isActiveAndEnabled) return;

            float now = Time.unscaledTime;
            if (_dynamicContextBatchWindowStartTime < 0f)
                _dynamicContextBatchWindowStartTime = now;

            float maxDeadline = _dynamicContextBatchWindowStartTime + DynamicContextMaxBatchDelaySeconds;
            _dynamicContextNextFlushTime = Mathf.Min(now + DynamicContextBatchDelaySeconds, maxDeadline);
            if (_dynamicContextFlushCoroutine != null) return;

            _dynamicContextFlushCoroutine = StartCoroutine(FlushDynamicContextAfterDelay());
        }

        private IEnumerator FlushDynamicContextAfterDelay()
        {
            while (true)
            {
                float remaining = _dynamicContextNextFlushTime - Time.unscaledTime;
                if (remaining <= 0f) break;

                yield return new WaitForSecondsRealtime(remaining);
            }

            _dynamicContextFlushCoroutine = null;
            FlushPendingContextUpdates();
        }

        private void StopScheduledDynamicContextFlush()
        {
            if (_dynamicContextFlushCoroutine == null) return;

            StopCoroutine(_dynamicContextFlushCoroutine);
            _dynamicContextFlushCoroutine = null;
        }

        private void ResetDynamicContextBatchTiming()
        {
            _dynamicContextBatchWindowStartTime = -1f;
            _dynamicContextNextFlushTime = -1f;
        }

        private string CreateDynamicContextUpdateId()
        {
            _dynamicContextUpdateSequence++;
            string characterId = string.IsNullOrWhiteSpace(CharacterId) ? "character" : CharacterId;
            return $"unity-{characterId}-{_dynamicContextUpdateSequence}";
        }

        private bool TrySendDynamicContextUpdate(ConvaiDynamicContextUpdate update, string purpose)
        {
            IConvaiDynamicContextTransport transport = DynamicContextTransport;
            if (transport == null)
            {
                Logger?.Warning(
                    $"[{_characterName}] Dynamic context transport unavailable for {purpose}");
                return false;
            }

            if (transport.SendDynamicContext(update)) return true;

            Logger?.Warning(
                $"[{_characterName}] Connection not ready for {purpose}");
            return false;
        }

        private bool TryValidateDynamicContextStateName(string name)
        {
            if (!string.IsNullOrWhiteSpace(name)) return true;

            Logger?.Warning($"[{_characterName}] Dynamic context state name cannot be empty");
            return false;
        }

        private bool TryValidateDynamicContextStateValue(string name, string value)
        {
            if (value != null) return true;

            Logger?.Warning(
                $"[{_characterName}] Dynamic context state '{name}' cannot use a null value");
            return false;
        }

        private bool TryValidateDynamicContextEventText(string text)
        {
            if (!string.IsNullOrWhiteSpace(text)) return true;

            Logger?.Warning($"[{_characterName}] Dynamic context event text cannot be empty");
            return false;
        }

        private bool TryValidateRawDynamicContextUpdate(ConvaiDynamicContextUpdate update)
        {
            if (update == null)
            {
                Logger?.Warning(
                    $"[ConvaiCharacter] [{_characterName}] Raw dynamic context update cannot be null");
                return false;
            }

            if (update.Mode == ConvaiContextUpdateMode.Reset ||
                update.Text != null ||
                update.ActionConfig != null ||
                update.CurrentAttentionObject != null)
                return true;

            Logger?.Warning(
                $"[ConvaiCharacter] [{_characterName}] Raw dynamic context updates require text, action_config, current_attention_object, or Reset mode");
            return false;
        }

        private bool TryPrepareRuntimeActionStateUpdate(
            ConvaiDynamicContextUpdate update,
            out ConvaiDynamicContextUpdate preparedUpdate,
            out ConvaiActionConfigReconciliation reconciliation,
            out bool hasRuntimeActionState,
            out string error)
        {
            preparedUpdate = update;
            reconciliation = default;
            error = string.Empty;
            hasRuntimeActionState = update.ActionConfig != null || update.CurrentAttentionObject != null;
            if (!hasRuntimeActionState)
                return true;

            if (!TryBuildPredictedRuntimeActionConfig(out ConvaiActionConfig predictedConfig, out error))
                return false;

            if (!ConvaiActionConfigPatchReconciler.TryReconcile(
                    predictedConfig,
                    update.ActionConfig,
                    update.CurrentAttentionObject,
                    GetRuntimeActionDefinitionCatalog(),
                    out reconciliation,
                    out error))
                return false;

            string updateId = string.IsNullOrWhiteSpace(update.UpdateId)
                ? CreateDynamicContextUpdateId()
                : update.UpdateId.Trim();
            for (int i = 0; i < _pendingRuntimeActionStateUpdates.Count; i++)
            {
                if (!string.Equals(
                        _pendingRuntimeActionStateUpdates[i].UpdateId,
                        updateId,
                        StringComparison.Ordinal))
                    continue;

                error = $"update_id '{updateId}' already has a pending runtime action mutation";
                return false;
            }

            preparedUpdate = update.WithRuntimeActionState(
                reconciliation.TopLevelAttentionObject,
                updateId,
                reconciliation.Patch);
            return true;
        }

        private bool TryBuildPredictedRuntimeActionConfig(
            out ConvaiActionConfig predictedConfig,
            out string error)
        {
            // Server-shared state only: local-only lookup aids (scene target components, groups,
            // GameObject bindings) must never leak into what we predict the backend knows.
            predictedConfig = GetServerSharedActionConfig()?.Clone() ?? new ConvaiActionConfig();
            error = string.Empty;
            for (int i = 0; i < _pendingRuntimeActionStateUpdates.Count; i++)
            {
                PendingRuntimeActionStateUpdate pending = _pendingRuntimeActionStateUpdates[i];
                if (ConvaiActionConfigPatchReconciler.TryReconcile(
                        predictedConfig,
                        pending.ActionConfig,
                        pending.TopLevelAttentionObject,
                        GetRuntimeActionDefinitionCatalog(),
                        out ConvaiActionConfigReconciliation reconciliation,
                        out error))
                {
                    predictedConfig = reconciliation.Snapshot;
                    continue;
                }

                error = $"pending update '{pending.UpdateId}' cannot be reconciled: {error}";
                return false;
            }

            return true;
        }

        /// <summary>
        ///     Editor-only friend-assembly seam for previewing the same reconciliation used by send
        ///     and ACK commit. Does not send or mutate runtime state.
        /// </summary>
        internal bool TryPreviewRuntimeActionStateUpdate(
            ConvaiActionConfigPatch actionConfig,
            object topLevelAttentionObject,
            out ConvaiActionConfig predictedConfig,
            out string error)
        {
            predictedConfig = null;
            if (!TryBuildPredictedRuntimeActionConfig(out ConvaiActionConfig current, out error))
                return false;

            if (!ConvaiActionConfigPatchReconciler.TryReconcile(
                    current,
                    actionConfig,
                    topLevelAttentionObject,
                    GetRuntimeActionDefinitionCatalog(),
                    out ConvaiActionConfigReconciliation reconciliation,
                    out error))
                return false;

            predictedConfig = reconciliation.Snapshot.Clone();
            return true;
        }

        /// <summary>Returns a detached snapshot for editor diagnostics.</summary>
        internal IReadOnlyList<ConvaiRuntimeActionUpdateDebugInfo> GetPendingRuntimeActionUpdateDebugInfo()
        {
            if (_pendingRuntimeActionStateUpdates.Count == 0)
                return Array.Empty<ConvaiRuntimeActionUpdateDebugInfo>();

            var snapshot = new List<ConvaiRuntimeActionUpdateDebugInfo>(
                _pendingRuntimeActionStateUpdates.Count);
            for (int i = 0; i < _pendingRuntimeActionStateUpdates.Count; i++)
            {
                PendingRuntimeActionStateUpdate pending = _pendingRuntimeActionStateUpdates[i];
                snapshot.Add(new ConvaiRuntimeActionUpdateDebugInfo(
                    pending.UpdateId,
                    pending.SentAtUtc,
                    pending.ActionConfig != null,
                    pending.TopLevelAttentionObject != null,
                    pending.HasAcknowledgement,
                    pending.HasAcknowledgement ? pending.Acknowledgement.Status : string.Empty));
            }

            return snapshot;
        }

        private void RegisterPendingRuntimeActionStateUpdate(
            ConvaiDynamicContextUpdate preparedUpdate,
            ConvaiActionConfigReconciliation reconciliation)
        {
            _pendingRuntimeActionStateUpdates.Add(new PendingRuntimeActionStateUpdate
            {
                UpdateId = preparedUpdate.UpdateId,
                ActionConfig = reconciliation.Patch?.Clone(),
                TopLevelAttentionObject = reconciliation.TopLevelAttentionObject,
                SentAtUtc = DateTime.UtcNow
            });

            if (_runtimeActionUpdateAckMonitorCoroutine == null && isActiveAndEnabled)
                _runtimeActionUpdateAckMonitorCoroutine = StartCoroutine(MonitorRuntimeActionUpdateAcks());
        }

        private void OnDynamicContextUpdateResultReceived(DynamicContextUpdateResultReceived result)
        {
            if (string.IsNullOrWhiteSpace(result.UpdateId))
                return;

            for (int i = 0; i < _pendingRuntimeActionStateUpdates.Count; i++)
            {
                PendingRuntimeActionStateUpdate pending = _pendingRuntimeActionStateUpdates[i];
                if (!string.Equals(pending.UpdateId, result.UpdateId, StringComparison.Ordinal))
                    continue;

                if (!pending.HasAcknowledgement)
                {
                    pending.Acknowledgement = result;
                    pending.AcknowledgedAtUtc = DateTime.UtcNow;
                    pending.HasAcknowledgement = true;
                }

                DrainPendingRuntimeActionStateUpdates(DateTime.UtcNow);
                return;
            }
        }

        private IEnumerator MonitorRuntimeActionUpdateAcks()
        {
            while (_pendingRuntimeActionStateUpdates.Count > 0)
            {
                yield return new WaitForSecondsRealtime(RuntimeActionUpdateAckPollSeconds);
                DrainPendingRuntimeActionStateUpdates(DateTime.UtcNow);
            }

            _runtimeActionUpdateAckMonitorCoroutine = null;
        }

        internal void ProcessPendingRuntimeActionStateUpdates(DateTime nowUtc) =>
            DrainPendingRuntimeActionStateUpdates(nowUtc);

        private void DrainPendingRuntimeActionStateUpdates(DateTime nowUtc)
        {
            while (_pendingRuntimeActionStateUpdates.Count > 0)
            {
                PendingRuntimeActionStateUpdate pending = _pendingRuntimeActionStateUpdates[0];
                DateTime completionTime = pending.HasAcknowledgement
                    ? pending.AcknowledgedAtUtc
                    : nowUtc;
                bool timedOut = completionTime - pending.SentAtUtc >=
                                TimeSpan.FromSeconds(RuntimeActionUpdateAckTimeoutSeconds);
                if (!pending.HasAcknowledgement && !timedOut)
                    break;

                _pendingRuntimeActionStateUpdates.RemoveAt(0);
                if (timedOut)
                {
                    WarnDiscardedRuntimeActionMutation(pending.UpdateId, "ack_timeout");
                    continue;
                }

                if (!TryCommitAcknowledgedRuntimeActionState(pending, out string reasonCode))
                {
                    WarnDiscardedRuntimeActionMutation(pending.UpdateId, reasonCode);
                    continue;
                }

                Logger?.Debug(
                    $"[{_characterName}] Runtime action state committed after ACK update_id={pending.UpdateId}");
            }

            if (_pendingRuntimeActionStateUpdates.Count == 0)
                StopRuntimeActionUpdateAckMonitor();
        }

        private bool TryCommitAcknowledgedRuntimeActionState(
            PendingRuntimeActionStateUpdate pending,
            out string reasonCode)
        {
            DynamicContextUpdateResultReceived acknowledgement = pending.Acknowledgement;
            if (!string.Equals(acknowledgement.Status, "success", StringComparison.OrdinalIgnoreCase))
            {
                reasonCode = "ack_error";
                return false;
            }

            if (!ConvaiActionConfigPatchReconciler.TryReconcile(
                    GetServerSharedActionConfig(),
                    pending.ActionConfig,
                    pending.TopLevelAttentionObject,
                    GetRuntimeActionDefinitionCatalog(),
                    out ConvaiActionConfigReconciliation reconciliation,
                    out _))
            {
                reasonCode = "ack_reconcile_failed";
                return false;
            }

            if (pending.ActionConfig != null &&
                !TryVerifyActionConfigAcknowledgement(acknowledgement, reconciliation.Snapshot, out reasonCode))
                return false;

            SetResolvedSessionActionConfig(reconciliation.Snapshot);
            IReadOnlyList<ConvaiActionDefinition> catalog = GetRuntimeActionDefinitionCatalog();
            IReadOnlyList<ConvaiActionDefinition> activeDefinitions =
                reconciliation.Snapshot.Actions == null || reconciliation.Snapshot.Actions.Count == 0
                    ? Array.Empty<ConvaiActionDefinition>()
                    : ConvaiActionDefinition.FilterAndClone(
                        catalog,
                        reconciliation.Snapshot.Actions,
                        requireExecutable: true);
            SetResolvedSessionActionDefinitions(activeDefinitions);

            if (string.Equals(
                    acknowledgement.ActionGenerationStrategyStatus,
                    "requires_reconnect",
                    StringComparison.OrdinalIgnoreCase))
            {
                Logger?.Warning(
                    $"[{_characterName}] Runtime action update ACK requires reconnect; no automatic reconnect performed (update_id={pending.UpdateId})");
            }

            reasonCode = string.Empty;
            return true;
        }

        private static bool TryVerifyActionConfigAcknowledgement(
            DynamicContextUpdateResultReceived acknowledgement,
            ConvaiActionConfig expected,
            out string reasonCode)
        {
            if (acknowledgement.ActionConfigUpdated != true ||
                !acknowledgement.ActionsCount.HasValue ||
                !acknowledgement.ObjectsCount.HasValue ||
                !acknowledgement.CharactersCount.HasValue ||
                acknowledgement.RawExtras == null ||
                !acknowledgement.RawExtras.TryGetValue("current_attention_object", out _))
            {
                reasonCode = "ack_malformed";
                return false;
            }

            if (acknowledgement.ActionsCount.Value != (expected.Actions?.Count ?? 0) ||
                acknowledgement.ObjectsCount.Value != (expected.Objects?.Count ?? 0) ||
                acknowledgement.CharactersCount.Value != (expected.Characters?.Count ?? 0) ||
                !string.Equals(
                    acknowledgement.CurrentAttentionObject,
                    expected.CurrentAttentionObject,
                    StringComparison.Ordinal))
            {
                reasonCode = "ack_metadata_mismatch";
                return false;
            }

            reasonCode = string.Empty;
            return true;
        }

        private void WarnDiscardedRuntimeActionMutation(string updateId, string reasonCode) =>
            Logger?.Warning(
                $"[{_characterName}] Runtime action mutation discarded update_id={updateId} reason={reasonCode}");

        private void StopRuntimeActionUpdateAckMonitor()
        {
            if (_runtimeActionUpdateAckMonitorCoroutine == null)
                return;

            StopCoroutine(_runtimeActionUpdateAckMonitorCoroutine);
            _runtimeActionUpdateAckMonitorCoroutine = null;
        }

        private void ClearPendingRuntimeActionStateUpdates()
        {
            StopRuntimeActionUpdateAckMonitor();
            _pendingRuntimeActionStateUpdates.Clear();
        }
    }
}
