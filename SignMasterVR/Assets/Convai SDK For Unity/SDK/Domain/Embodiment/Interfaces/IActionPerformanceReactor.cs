using UnityEngine;

namespace Convai.Domain.Embodiment.Interfaces
{
    /// <summary>
    ///     Contract for a peer embodiment module that reacts to action-batch lifecycle events for
    ///     realism (an acknowledgment nod, a look-where-you-act glance, a brief outcome mood beat).
    ///     The actions dispatcher raises these through the character's embodiment context; when no
    ///     reactor is registered the calls are no-ops.
    /// </summary>
    /// <remarks>
    ///     Unlike the single-owner seams on <see cref="Convai.Runtime.Embodiment.EmbodimentContext" />
    ///     (one source per role, e.g. <see cref="IHeadGestureChannel" />), this seam is
    ///     <b>multicast</b>: Gaze, Body Language, and Emotion can all register a reactor at once and
    ///     each is notified independently, because they react to the same event in unrelated ways
    ///     (a glance, a nod, a mood nudge). Implementations must be cheap and side-effect-local —
    ///     they are called from the actions dispatcher's step loop, not a per-frame tick.
    /// </remarks>
    internal interface IActionPerformanceReactor
    {
        /// <summary>
        ///     Raised once per batch, after the speech gate has released and before the first
        ///     step executes — the "I heard you, I'm on it" moment.
        /// </summary>
        void OnActionBatchStarted();

        /// <summary>
        ///     Raised when a step starts with a resolved world-space target, so a reactor can look
        ///     toward what the character is about to act on.
        /// </summary>
        /// <param name="targetName">Display name of the resolved target.</param>
        /// <param name="worldPosition">World-space point to look/act toward.</param>
        void OnActionTargetAcquired(string targetName, Vector3 worldPosition);

        /// <summary>
        ///     Raised after a step concludes with <paramref name="success" /> indicating whether it
        ///     succeeded. Reactors that acquired a resource in
        ///     <see cref="OnActionTargetAcquired" /> (e.g. a held gaze glance) should release it here.
        /// </summary>
        void OnActionOutcome(bool success);
    }
}
