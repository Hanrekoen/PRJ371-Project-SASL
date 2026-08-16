using Convai.Domain.Embodiment.Semantics;
using Convai.Modules.Gaze.Data;
using UnityEngine;

namespace Convai.Modules.Gaze.Core.Policy
{
    /// <summary>
    ///     The per-tick instruction handed from the policy engine to the solver chain:
    ///     what to look at and how strongly each body stage should participate.
    /// </summary>
    internal struct GazeDirective
    {
        public GazeTargetKind Kind;
        public Transform Target;
        public Vector3 WorldPoint;

        /// <summary>Effective engagement (state policy × target commitment, smoothed).</summary>
        public float Engagement;

        /// <summary>
        ///     The engagement this look is heading for — the smoothed state policy (or a scripted
        ///     override) <i>without</i> the arbiter's acquire/release ramp.
        /// </summary>
        /// <remarks>
        ///     This is what the actuator ladder divides across the rungs, and the distinction is
        ///     the difference between a shaped movement and a fast one. The ladder's share is
        ///     proportional to engagement, so feeding it the ramped value made the head's goal
        ///     ramp over <c>commitmentAcquireSeconds</c> — and the tracking lane is transparent to
        ///     in-budget motion, so the head simply followed that ramp at the ramp's own speed,
        ///     with no duration law and no velocity profile. The acquisition ramp says the
        ///     character is taking the look up, not how fast its neck moves; how fast belongs to
        ///     the actuator, which needs to see the goal it is heading for in order to choose a
        ///     duration for getting there. See <c>GazeActuatorLadder</c>'s "two opinions about one
        ///     movement".
        /// </remarks>
        public float SettledEngagement;

        /// <summary>Head participation 0–1 (smoothed).</summary>
        public float HeadContribution;

        /// <summary>Whether the reorientation director may fire a body turn this frame.</summary>
        public bool AllowBodyTurn;

        public GazeAversionMode AversionMode;
        public float AversionStrength;
        public float FixationLiveliness;

        /// <summary>
        ///     How much the engaged target owns the aim (0–1), before the state policy's own
        ///     strength is applied. This is the arbiter's acquire/release ramp, not an amplitude:
        ///     a soft glance commits fully at half <see cref="Engagement" />. It is what the
        ///     idle-life hand-over is weighted by, so the head leaves the fixation it was holding
        ///     and returns to it continuously instead of being dropped to centre in one frame.
        /// </summary>
        public float TargetCommitment;

        /// <summary>Bumps on re-targets/teleports so the eye stage issues a fresh saccade.</summary>
        public int GenerationId;
        public bool TeleportedThisTick;

        /// <summary>
        ///     The aim jumped because the world did (camera cut / teleported target), not because
        ///     the character decided to look elsewhere. Only this counts as a reflex; see
        ///     <see cref="Targeting.GazeTargetDecision.WasCut" />.
        /// </summary>
        public bool WasCut;

        public string TargetName;

        public bool HasEngagedTarget =>
            Kind != GazeTargetKind.None && Kind != GazeTargetKind.Ambient && Engagement > 0.0001f;

        public static GazeDirective Disengaged => new()
        {
            Kind = GazeTargetKind.None,
            Engagement = 0f,
            HeadContribution = 0f,
            FixationLiveliness = 1f,
            TargetName = "-"
        };
    }
}
