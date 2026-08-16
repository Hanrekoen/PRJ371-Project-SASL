using Convai.Domain.Embodiment.Semantics;
using Convai.Modules.BodyAnimation.Core.Diagnostics;
using Convai.Modules.BodyAnimation.Core.Locomotion;
using UnityEngine;
using UnityEngine.AI;

namespace Convai.Modules.BodyAnimation.Core.Policy
{
    /// <summary>
    ///     The runtime wiring around <see cref="SocialSpacingPolicy" /> — NavMesh
    ///     sampling and the actual reposition command — plus the policy instance's own lifecycle.
    ///     <see cref="SocialSpacingPolicy" /> stays pure decision logic with no NavMesh/locomotion
    ///     calls; this class is what <c>ConvaiBodyAnimationController.TickSocialSpacing</c> used to
    ///     be.
    /// </summary>
    internal sealed class SocialSpacingRunner
    {
        /// <summary>NavMesh sample radius around a social-spacing target point.</summary>
        private const float SocialSpacingSampleRadius = 1f;

        private SocialSpacingPolicy _policy;

        /// <summary>
        ///     (Re)creates the policy — called from every place that used to construct
        ///     <c>SocialSpacingPolicy</c> directly (first build, set-swap handoff, an in-place
        ///     config apply). A fresh instance resets the hysteresis/budget state exactly as the
        ///     earlier inline construction did.
        /// </summary>
        internal void Rebuild(float comfortRadius, float comfortHoldSeconds, int maxRepositionsPerMinute) =>
            _policy = new SocialSpacingPolicy(comfortRadius, comfortHoldSeconds, maxRepositionsPerMinute);

        /// <summary>Drops the policy — call from teardown so a genuinely-missing policy degrades silently.</summary>
        internal void Clear() => _policy = null;

        /// <summary>
        ///     Evaluates the policy and, when it fires, samples the resulting target onto the
        ///     NavMesh and issues the reposition through the locomotion drive's short-move/settle
        ///     path. A failed NavMesh sample skips silently — the policy already spent the
        ///     attempt's budget token, so a cornered character does not retry-spam.
        /// </summary>
        internal void Tick(
            Transform characterRoot,
            ILocomotionDrive locomotion,
            AnimTrace trace,
            DialogueState dialogueState,
            float deltaTime,
            bool isBusy,
            bool hasConversantAnchor,
            Vector3 conversantPosition)
        {
            if (_policy == null || locomotion == null || characterRoot == null) return;
            if (!hasConversantAnchor) return;

            Vector3 characterPosition = characterRoot.position;
            float distance = Vector3.Distance(characterPosition, conversantPosition);

            bool fired = _policy.Tick(
                distance, characterPosition, conversantPosition, dialogueState, isBusy, deltaTime,
                out Vector3 targetPosition);
            if (!fired) return;

            if (!NavMesh.SamplePosition(targetPosition, out NavMeshHit hit, SocialSpacingSampleRadius, NavMesh.AllAreas))
            {
                if (trace is { IsDetail: true })
                    trace.Detail(
                        "Social spacing: no NavMesh within " +
                        $"{SocialSpacingSampleRadius:F1}m of the reposition target — skipping (attempt still counted).");
                return;
            }

            if (locomotion is IConvaiLocomotionCommands commands && commands.MoveTo(hit.position))
                trace?.State("Social spacing: repositioning — conversant crowded personal space.");
        }
    }
}
