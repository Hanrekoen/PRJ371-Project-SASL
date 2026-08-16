using System.Collections.Generic;
using Convai.Modules.BodyAnimation.Core.Layers;

namespace Convai.Modules.BodyAnimation.Core.Lifecycle
{
    /// <summary>
    ///     The two small, genuinely stateless-adjacent pieces of the set-swap handoff that
    ///     don't have to live on the controller — the retiring layer stack's lifetime, and the
    ///     grace/force escalation timing decision.
    /// </summary>
    /// <remarks>
    ///     The rest of <c>TryBeginSetHandoff</c>/<c>TickSetHandoff</c> stays on the controller: the
    ///     escalation timestamps (<c>_setSwapQueuedAt</c>/<c>_setSwapGraceIssued</c>) are pinned by
    ///     name via reflection in <c>BodyAnimationLifecycleTests</c>' white-box fakes (the same
    ///     constraint documented on <see cref="DeferredRequestSlot" />), <c>TickSetHandoff(float)</c>
    ///     itself is invoked by name the same way, and the surrounding orchestration (graph host,
    ///     live layer references, gesture-performer registration, the co-speech planner, the
    ///     public <c>ActionEvent</c>/<c>StateChanged</c> events) is the controller's own graph-
    ///     lifetime state — the same state <see cref="LayerStackBuilder" /> was extracted to build,
    ///     not to own.
    /// </remarks>
    internal sealed class AnimationSetHandoffCoordinator
    {
        private List<IAnimationLayer> _retiringLayers;

        /// <summary>Snapshots the outgoing layer stack as "retiring" — torn down once the crossfade completes.</summary>
        internal void BeginRetiring(List<IAnimationLayer> outgoingLayers) =>
            _retiringLayers = new List<IAnimationLayer>(outgoingLayers);

        /// <summary>
        ///     Tears down and clears any retiring layer stack. Called both when a handoff
        ///     crossfade completes and from <c>TeardownRuntime</c> — the exact same block used to
        ///     be duplicated at both call sites.
        /// </summary>
        internal void TeardownRetiringLayers()
        {
            if (_retiringLayers == null) return;
            for (int i = 0; i < _retiringLayers.Count; i++)
                _retiringLayers[i].Teardown();
            _retiringLayers = null;
        }

        /// <summary>
        ///     The grace/force escalation decision — pure, given how long a swap has been
        ///     queued and whether grace was already issued for it.
        /// </summary>
        internal static EscalationAction EvaluateEscalation(
            float elapsedSeconds, bool graceAlreadyIssued, float graceSeconds, float forceSeconds)
        {
            bool issueGrace = !graceAlreadyIssued && elapsedSeconds >= graceSeconds;
            bool force = elapsedSeconds >= forceSeconds;
            return new EscalationAction(issueGrace, force);
        }
    }

    /// <summary>What <see cref="AnimationSetHandoffCoordinator.EvaluateEscalation" /> decided for this tick.</summary>
    internal readonly struct EscalationAction
    {
        /// <summary>True exactly once per queued swap: ask blocking owners to yield.</summary>
        public readonly bool IssueGrace;

        /// <summary>True once the swap must proceed regardless of blockers.</summary>
        public readonly bool Force;

        public EscalationAction(bool issueGrace, bool force)
        {
            IssueGrace = issueGrace;
            Force = force;
        }
    }
}
