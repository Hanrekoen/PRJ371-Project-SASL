using System;
using Convai.Domain.Embodiment.Interfaces;
using Convai.Modules.BodyAnimation.Core.Layers;
using Convai.Modules.BodyAnimation.Data;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Core
{
    /// <summary>
    ///     BodyAnimation's implementation of <see cref="IConversationalGesturePerformer" />.
    ///     Resolves a <see cref="GestureCue" /> against the active <see cref="ConvaiBodyAnimationSet" />
    ///     and routes accepted cues through the existing <see cref="ActionLayer" /> play path —
    ///     interrupt policy, masking, and locomotion suspension are unchanged; this type adds no
    ///     new playback behavior, only a semantic entry point plus a suppression report.
    /// </summary>
    /// <remarks>
    ///     Owned by <see cref="Components.ConvaiBodyAnimationController" />: constructed once the
    ///     runtime is built (after the layers, so the layer's lifecycle delegate is already
    ///     assigned), registered on <c>EmbodimentContext</c> in <c>OnEnable</c>, detached and
    ///     unregistered in <c>OnDisable</c> and on every runtime rebuild. Refusals are silent
    ///     (no per-call log) per the module's degradation policy — a peer module polling
    ///     <see cref="TryPerform" /> every cue is expected to see frequent, unremarkable
    ///     <c>false</c> results. <see cref="Completed" /> is raised synchronously from the
    ///     layer's lifecycle notifications, i.e. always on the main thread.
    /// </remarks>
    internal sealed class ConversationalGesturePerformer : IConversationalGesturePerformer, IConversationalMotionBudget
    {
        /// <summary>
        ///     Cue playback must always terminate on its own: a cue-tagged
        ///     <see cref="ActionLoopMode.HoldUntilStopped" /> entry (e.g. the default set's
        ///     "think") is auto-stopped after this many seconds of its main loop, because the
        ///     performer contract has no stop call. Ignored by non-hold entries.
        /// </summary>
        internal const float CueHoldSeconds = 4f;

        /// <summary>Clamp floor for a peer's <see cref="ReportConversationalIntensity" /> report.</summary>
        internal const float MinIntensityScale = 0.7f;

        /// <summary>Clamp ceiling for a peer's <see cref="ReportConversationalIntensity" /> report.</summary>
        internal const float MaxIntensityScale = 1.3f;

        private static readonly ActionPlayOptions CuePlayOptions = new() { HoldSeconds = CueHoldSeconds };

        private readonly ConvaiBodyAnimationSet _set;
        private readonly ActionLayer _actionLayer;
        private readonly TalkLayer _talkLayer;
        private readonly LocomotionLayer _locomotionLayer;
        private readonly Action<BodyAnimationActionEvent> _lifecycleHandler;

        private GestureCue _pendingCue;
        private string _pendingActionName;
        private float _reportedIntensity = 1f;

        /// <summary>
        ///     Below-window threshold: gesture cues are refused
        ///     while <see cref="UpperBodyOccupancy01" /> is above this — the overlay dips below
        ///     it in the brief pauses between phrases, which is exactly when a semantic clip may
        ///     fire without visibly fighting the authored talk content.
        /// </summary>
        internal const float CueOccupancyWindowThreshold = 0.35f;

        public ConversationalGesturePerformer(
            ConvaiBodyAnimationSet set,
            ActionLayer actionLayer,
            TalkLayer talkLayer,
            LocomotionLayer locomotionLayer)
        {
            _set = set;
            _actionLayer = actionLayer;
            _talkLayer = talkLayer;
            _locomotionLayer = locomotionLayer;

            _lifecycleHandler = HandleActionLifecycle;
            if (_actionLayer != null)
                _actionLayer.LifecycleChanged += _lifecycleHandler;
        }

        /// <inheritdoc />
        public event Action<GestureCue, GesturePerformanceResult> Completed;

        /// <inheritdoc />
        public GestureSuppression CurrentSuppression => ComputeSuppression(
            _actionLayer != null && _actionLayer.IsRunningFullBodyAction,
            _locomotionLayer != null && _locomotionLayer.IsTurningInPlace,
            _talkLayer != null && _talkLayer.IsRunningFullBodyCoverage,
            _locomotionLayer != null && _locomotionLayer.IsMoving,
            _talkLayer != null && _talkLayer.IsRunningUpperBodyCoverage);

        /// <summary>
        ///     <inheritdoc cref="IConversationalMotionBudget.UpperBodyOccupancy01" />
        ///     Deliberately the LIVE, energy-scaled talk-layer weight (the greater of the
        ///     stationary and moving-overlay ports) rather than the envelope — it dips toward
        ///     zero in speech pauses even while <see cref="TalkLayer.IsRunningUpperBodyCoverage" />
        ///     stays true, and those dips are exactly the windows <see cref="TryPerform" />'s own
        ///     occupancy-window check (below) and a peer module's procedural motion may use.
        /// </summary>
        public float UpperBodyOccupancy01 =>
            _talkLayer == null ? 0f : Mathf.Clamp01(Mathf.Max(_talkLayer.Weight, _talkLayer.MovingWeight));

        /// <inheritdoc />
        public GestureSuppression HardSuppression => ComputeHardSuppression(
            _actionLayer != null && _actionLayer.IsRunningFullBodyAction,
            _locomotionLayer != null && _locomotionLayer.IsTurningInPlace,
            _talkLayer != null && _talkLayer.IsRunningFullBodyCoverage,
            _locomotionLayer != null && _locomotionLayer.IsMoving);

        /// <summary>Last <see cref="ReportConversationalIntensity" /> value, clamped. Held (never decays) until re-reported.</summary>
        internal float ReportedIntensityScale => _reportedIntensity;

        /// <summary>
        ///     Pure suppression policy: full-body ownership beats upper-body
        ///     business beats none. Exercised directly by the EditMode truth-table tests so the
        ///     tested logic is the shipped logic.
        /// </summary>
        internal static GestureSuppression ComputeSuppression(
            bool fullBodyAction,
            bool turningInPlace,
            bool talkFullBodyCoverage,
            bool moving,
            bool upperBodyTalk)
        {
            if (fullBodyAction || turningInPlace || talkFullBodyCoverage)
                return GestureSuppression.FullBody;

            return moving || upperBodyTalk ? GestureSuppression.UpperBody : GestureSuppression.None;
        }

        /// <summary>
        ///     Pure HARD suppression policy: the subset of <see cref="ComputeSuppression" />
        ///     no budget negotiation overrides. Locomotion still hard-suppresses (arms stay busy
        ///     with the gait); upper-body talk ALONE is deliberately <see cref="GestureSuppression.None" />
        ///     here — <see cref="UpperBodyOccupancy01" /> covers that case at finer granularity via
        ///     <see cref="TryPerform" />'s own occupancy-window check. Exercised directly by the
        ///     EditMode truth-table tests so the tested logic is the shipped logic.
        /// </summary>
        internal static GestureSuppression ComputeHardSuppression(
            bool fullBodyAction,
            bool turningInPlace,
            bool talkFullBodyCoverage,
            bool moving)
        {
            if (fullBodyAction || turningInPlace || talkFullBodyCoverage)
                return GestureSuppression.FullBody;

            return moving ? GestureSuppression.UpperBody : GestureSuppression.None;
        }

        /// <inheritdoc />
        public void ReportConversationalIntensity(float intensityScale) =>
            _reportedIntensity = Mathf.Clamp(intensityScale, MinIntensityScale, MaxIntensityScale);

        /// <inheritdoc />
        public bool TryPerform(in GestureCue cue)
        {
            if (cue.Kind == GestureCueKind.None) return false;
            if (_set == null || _actionLayer == null) return false;

            GestureSuppression hard = HardSuppression;
            if (hard != GestureSuppression.None) return false; // clips off under any HARD suppression
            if (UpperBodyOccupancy01 > CueOccupancyWindowThreshold) return false; // cues fire in speech pauses

            if (!_set.TryGetActionForCue(cue.Kind, out ActionEntry entry)) return false;
            if (_actionLayer.IsBusyNonInterruptible) return false;

            // Play fires Interrupted for a replaced entry synchronously in here, which settles
            // any still-pending cue through HandleActionLifecycle before we track the new one.
            BodyAnimationActionHandle handle = _actionLayer.Play(entry, in CuePlayOptions);
            if (handle == null) return false; // rejected by the layer's own interrupt policy

            _pendingCue = cue;
            _pendingActionName = handle.ActionName;
            return true;
        }

        /// <summary>
        ///     Unhooks the layer and settles a still-pending cue as
        ///     <see cref="GesturePerformanceResult.Cancelled" />. The owning controller calls
        ///     this before the runtime (and this performer with it) is torn down.
        /// </summary>
        public void Detach()
        {
            if (_actionLayer != null)
                _actionLayer.LifecycleChanged -= _lifecycleHandler;

            if (_pendingActionName == null) return;
            GestureCue cue = _pendingCue;
            _pendingActionName = null;
            Completed?.Invoke(cue, GesturePerformanceResult.Cancelled);
        }

        private void HandleActionLifecycle(BodyAnimationActionEvent actionEvent)
        {
            if (_pendingActionName == null) return;

            bool terminal = actionEvent.Phase == BodyAnimationActionPhase.Completed ||
                            actionEvent.Phase == BodyAnimationActionPhase.Interrupted;
            if (!terminal) return;
            if (!string.Equals(actionEvent.ActionName, _pendingActionName, StringComparison.Ordinal)) return;

            GestureCue cue = _pendingCue;
            _pendingActionName = null;
            Completed?.Invoke(cue, actionEvent.Phase == BodyAnimationActionPhase.Completed
                ? GesturePerformanceResult.Completed
                : GesturePerformanceResult.Interrupted);
        }
    }
}
