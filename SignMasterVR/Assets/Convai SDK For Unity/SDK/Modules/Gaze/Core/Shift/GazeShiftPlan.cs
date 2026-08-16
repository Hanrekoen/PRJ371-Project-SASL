using UnityEngine;

namespace Convai.Modules.Gaze.Core.Shift
{
    /// <summary>
    ///     How deep the actuator ladder is recruited for the shift being executed. Reported for
    ///     diagnostics; the plan's angles are what actually drives the rig.
    /// </summary>
    internal enum GazeLadderDepth
    {
        /// <summary>Nothing is engaged.</summary>
        Idle = 0,

        /// <summary>Small shift: the eyes handle it alone.</summary>
        Eyes = 1,

        /// <summary>The head joins.</summary>
        Head = 2,

        /// <summary>The torso joins.</summary>
        Torso = 3,

        /// <summary>The feet are asked to close the rest.</summary>
        Feet = 4
    }

    /// <summary>
    ///     One frame's division of a gaze shift across the actuator ladder. Every angle is
    ///     degrees in the character-root frame.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The eyes deliberately have no field here. They are the <i>residual</i>: their
    ///         angles are already expressed relative to the head-carried rest forward, so
    ///         whatever the head, torso and feet have not yet delivered is what the eyes are
    ///         holding, automatically and without a second opinion about the size of the shift.
    ///         That is what makes conservation structural rather than something to keep in sync.
    ///     </para>
    ///     <para>
    ///         It follows that the ladder's real job is to make sure the lower rungs eventually
    ///         absorb enough that the residual — and therefore the eyes' deviation from centre —
    ///         falls back toward zero.
    ///     </para>
    /// </remarks>
    internal readonly struct GazeShiftPlan
    {
        /// <summary>Yaw asked of the neck+head chain.</summary>
        public readonly float HeadYaw;

        /// <summary>Pitch asked of the neck+head chain.</summary>
        public readonly float HeadPitch;

        /// <summary>Yaw asked of chest+upper-chest.</summary>
        public readonly float TorsoYaw;

        /// <summary>Pitch asked of chest+upper-chest.</summary>
        public readonly float TorsoPitch;

        /// <summary>
        ///     Yaw the head and torso together could not take — what is left for the feet.
        ///     Meaningful whether or not <see cref="WantsFeet" /> is set: it is the quantity the
        ///     feet decision is made on, and it is worth seeing in a trace either way.
        /// </summary>
        public readonly float FeetResidualYaw;

        /// <summary>Whether the ladder is asking for a body turn this frame.</summary>
        public readonly bool WantsFeet;

        /// <summary>Deepest rung recruited, for diagnostics.</summary>
        public readonly GazeLadderDepth Depth;

        /// <summary>
        ///     Whether the head has joined this look at all (0–1): the entry ease times the onset
        ///     gate, and deliberately <i>not</i> engagement or willingness — those say how much of
        ///     the shift the head takes, not whether it is taking part yet.
        /// </summary>
        /// <remarks>
        ///     Before the head joins, the look is being made by the eyes alone; that is what the
        ///     onset gap means. Note this is a <i>product</i> of two factors that mean different
        ///     things — see <see cref="HeadOnsetPending" />, which is the one the idle-life
        ///     hand-over must read.
        /// </remarks>
        public readonly float HeadRecruitment;

        /// <summary>
        ///     Whether the head is a rung of <i>this</i> shift that simply has not started yet:
        ///     the amplitude clears the head's entry angle, but the onset has not elapsed.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         This is what the head actuator's hand-over from idle life keys on, and the
        ///         distinction from <see cref="HeadRecruitment" /> being zero is the whole point.
        ///         Recruitment is <c>entry × onset</c>, and those two zeros say opposite things:
        ///     </para>
        ///     <list type="bullet">
        ///         <item>
        ///             <description>
        ///                 <b>onset zero</b> — the head <i>will</i> join, in a moment. It is still
        ///                 holding the fixation idle life gave it, and handing that fixation back
        ///                 is correct: dropping it made the head travel to centre and out again
        ///                 for every look taken out of idle.
        ///             </description>
        ///         </item>
        ///         <item>
        ///             <description>
        ///                 <b>entry zero</b> — the shift is below the head's entry angle, so the
        ///                 head is <i>never</i> joining this look; the eyes own it, permanently.
        ///                 There is no onset gap to bridge and no idle life to resume. Reading
        ///                 the product here drove the head's goal to a stale ambient fixation
        ///                 (or, once the resume window had cleared it, to rest-forward) for the
        ///                 whole of any commitment dip during a settled conversation — which is
        ///                 the steady state of talking to someone you are already facing.
        ///             </description>
        ///         </item>
        ///     </list>
        /// </remarks>
        public readonly bool HeadOnsetPending;

        public GazeShiftPlan(
            float headYaw,
            float headPitch,
            float torsoYaw,
            float torsoPitch,
            float feetResidualYaw,
            bool wantsFeet,
            GazeLadderDepth depth,
            float headRecruitment = 0f,
            bool headOnsetPending = false)
        {
            HeadRecruitment = headRecruitment;
            HeadOnsetPending = headOnsetPending;
            HeadYaw = headYaw;
            HeadPitch = headPitch;
            TorsoYaw = torsoYaw;
            TorsoPitch = torsoPitch;
            FeetResidualYaw = feetResidualYaw;
            WantsFeet = wantsFeet;
            Depth = depth;
        }

        /// <summary>Nothing engaged: every rung at rest.</summary>
        public static GazeShiftPlan Idle =>
            new(0f, 0f, 0f, 0f, 0f, false, GazeLadderDepth.Idle);

        /// <summary>Head contribution as a vector, for the actuator that consumes it.</summary>
        public Vector2 Head => new(HeadYaw, HeadPitch);

        /// <summary>Torso contribution as a vector.</summary>
        public Vector2 Torso => new(TorsoYaw, TorsoPitch);
    }
}
