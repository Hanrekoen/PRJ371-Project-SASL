using Convai.Domain.Embodiment.Semantics;
using Convai.Modules.Gaze.Core.Targeting;
using Convai.Modules.Gaze.Data;
using UnityEngine;

namespace Convai.Modules.Gaze.Core.Policy
{
    /// <summary>
    ///     Converts the arbiter's target decision plus an already-resolved
    ///     <see cref="GazeStatePolicy" /> into a smoothed <see cref="GazeDirective" /> for
    ///     the solver chain. This is the single place where "how strongly should the
    ///     character look" is decided; solvers never consult dialogue state themselves.
    /// </summary>
    /// <remarks>
    ///     Policy resolution (dialogue state → <see cref="GazeStatePolicy" />, including any
    ///     hard override such as an eye-contact lock) happens once in
    ///     the caller and is passed in, so the same resolved policy also gates the arbiter's
    ///     target candidacy — there is exactly one source of truth per tick. Emotion and
    ///     speech-energy modulation plug in here: they scale the resolved policy
    ///     before smoothing, so every downstream stage inherits them for free.
    /// </remarks>
    internal sealed class GazePolicyEngine
    {
        private float _smoothedEngagement;
        private bool _initialized;

        /// <summary>State policy resolved on the last tick (for diagnostics).</summary>
        public GazeStatePolicy ActivePolicy { get; private set; }

        /// <summary>Smoothed policy engagement before target commitment (for diagnostics).</summary>
        public float SmoothedEngagement => _smoothedEngagement;

        /// <summary>
        ///     Emotion-modulation hook. Multiplies the resolved engagement before
        ///     smoothing; 1 = no modulation.
        /// </summary>
        public float EngagementModifier { get; set; } = 1f;

        /// <summary>
        ///     Emotion-modulation hook. Multiplies the resolved aversion strength;
        ///     1 = no modulation.
        /// </summary>
        public float AversionModifier { get; set; } = 1f;

        public GazeDirective Tick(
            in GazeStatePolicy policy,
            in GazeTargetDecision decision,
            ConvaiGazeProfile profile,
            float deltaTime)
        {
            ActivePolicy = policy;

            float targetEngagement = policy.Engagement * EngagementModifier;
            if (decision.IsScripted && decision.ScriptedEngagementOverride >= 0f)
                targetEngagement = decision.ScriptedEngagementOverride;
            targetEngagement = Mathf.Clamp01(targetEngagement);

            // How much of the shift the head takes. A scripted request may state its own — an
            // idle glance at a person is an act of attention and the head does most of it,
            // whatever the state policy asks for while the character is drifting.
            //
            // Not smoothed, unlike engagement: this value's only consumer is the actuator
            // ladder, so it is always a movement's GOAL and never a gain, and a goal that
            // arrives over a fifth of a second is tracked instead of planned. Engagement is
            // smoothed because it is also the stabilization reflex's gain, which must ease.
            float targetHead = decision.IsScripted && decision.ScriptedHeadContributionOverride >= 0f
                ? Mathf.Clamp01(decision.ScriptedHeadContributionOverride)
                : Mathf.Clamp01(policy.HeadContribution);

            if (!_initialized)
            {
                _smoothedEngagement = targetEngagement;
                _initialized = true;
            }
            else
            {
                float alpha = 1f - Mathf.Exp(-Mathf.Max(0f, profile.PolicyBlendSpeed) * deltaTime);
                _smoothedEngagement += (targetEngagement - _smoothedEngagement) * alpha;
            }

            bool hasTarget = decision.HasTarget;
            float effectiveEngagement = hasTarget ? _smoothedEngagement * decision.Commitment : 0f;

            return new GazeDirective
            {
                Kind = hasTarget ? decision.Kind : GazeTargetKind.None,
                Target = hasTarget ? decision.Target : null,
                WorldPoint = decision.SmoothedPoint,
                Engagement = effectiveEngagement,
                // The destination strength, unsmoothed and unramped: what this look is worth once
                // the character has taken it up. The smoothing below it is a GAIN easing — right
                // for the stabilization reflex and for gating, wrong as a movement's goal, because
                // a goal that arrives over 0.2 s is tracked rather than planned. The actuator
                // ladder is the one consumer that needs the destination.
                SettledEngagement = hasTarget ? targetEngagement : 0f,
                TargetCommitment = hasTarget ? Mathf.Clamp01(decision.Commitment) : 0f,
                HeadContribution = targetHead,
                AllowBodyTurn = decision.IsScripted
                    ? decision.ScriptedAllowBodyTurn
                    : policy.AllowBodyTurn,
                AversionMode = policy.AversionMode,
                AversionStrength = Mathf.Clamp01(policy.AversionStrength * AversionModifier),
                FixationLiveliness = policy.FixationLiveliness,
                GenerationId = decision.GenerationId,
                TeleportedThisTick = decision.TeleportedThisTick,
                WasCut = decision.WasCut,
                TargetName = decision.Name
            };
        }

        public void Reset()
        {
            _smoothedEngagement = 0f;
            _initialized = false;
            EngagementModifier = 1f;
            AversionModifier = 1f;
            ActivePolicy = default;
        }
    }
}
