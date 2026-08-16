using Convai.Modules.Gaze.Core.Solvers;
using UnityEngine;

namespace Convai.Modules.Gaze.Core.Shift
{
    /// <summary>
    ///     What each rung of the ladder is physically able to contribute, and how willing this
    ///     character is to use it. Separated from the ladder itself so the distribution rule can
    ///     be tested against arbitrary rigs and personalities without a profile or a scene.
    /// </summary>
    internal readonly struct GazeLadderCapacity
    {
        public readonly float HeadYaw;
        public readonly float HeadPitch;
        public readonly float TorsoYaw;
        public readonly float TorsoPitch;

        /// <summary>0–1: how much of a shift this character's head is willing to take at all.</summary>
        public readonly float HeadWillingness;

        /// <summary>
        ///     Head yaw (degrees) the neck can hold without wanting relief. Used as the head's
        ///     cap while the feet cannot help — see the ladder's remarks.
        /// </summary>
        public readonly float HeadComfortYaw;

        /// <summary>False when the rig has no torso bones, or the character never uses them.</summary>
        public readonly bool TorsoAvailable;

        /// <summary>False while something else owns the character's facing (walking a path, scripted).</summary>
        public readonly bool FeetAvailable;

        public GazeLadderCapacity(
            float headYaw,
            float headPitch,
            float torsoYaw,
            float torsoPitch,
            float headWillingness,
            float headComfortYaw,
            bool torsoAvailable,
            bool feetAvailable)
        {
            HeadYaw = headYaw;
            HeadPitch = headPitch;
            TorsoYaw = torsoYaw;
            TorsoPitch = torsoPitch;
            HeadWillingness = headWillingness;
            HeadComfortYaw = headComfortYaw;
            TorsoAvailable = torsoAvailable;
            FeetAvailable = feetAvailable;
        }
    }

    /// <summary>
    ///     Entry angles and onset delays for each rung — the ladder's tuning, read straight off
    ///     the profile.
    /// </summary>
    internal readonly struct GazeLadderTuning
    {
        public readonly float HeadEntryDegrees;
        public readonly float TorsoEntryDegrees;
        public readonly float FeetEntryDegrees;
        public readonly float HeadOnsetSeconds;
        public readonly float TorsoOnsetSeconds;
        public readonly float FeetOnsetSeconds;

        public GazeLadderTuning(
            float headEntryDegrees,
            float torsoEntryDegrees,
            float feetEntryDegrees,
            float headOnsetSeconds,
            float torsoOnsetSeconds,
            float feetOnsetSeconds)
        {
            HeadEntryDegrees = headEntryDegrees;
            TorsoEntryDegrees = torsoEntryDegrees;
            FeetEntryDegrees = feetEntryDegrees;
            HeadOnsetSeconds = headOnsetSeconds;
            TorsoOnsetSeconds = torsoOnsetSeconds;
            FeetOnsetSeconds = feetOnsetSeconds;
        }
    }

    /// <summary>
    ///     Divides one gaze shift across eyes → head → torso → feet.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Stateless and engine-free by design: given the shift, what the rig can do, and how
    ///         long the shift has been running, there is exactly one right answer, and it can be
    ///         checked without a rig, a scene or a frame loop. All the state a gaze shift has —
    ///         when it started, what the actuators achieved — lives in
    ///         <see cref="GazeShiftDirector" />.
    ///     </para>
    ///     <para>
    ///         <b>The distribution rule.</b> Rungs are recruited in order and each one is handed
    ///         only what the rungs above it could not take:
    ///     </para>
    ///     <list type="number">
    ///         <item><description>the head takes its willing share of the shift, capped by its range;</description></item>
    ///         <item><description>the torso takes what is left, capped by its (much smaller) range;</description></item>
    ///         <item><description>the feet are asked to close whatever still remains in yaw.</description></item>
    ///     </list>
    ///     <para>
    ///         The eyes are not in that list because they are the residual — see
    ///         <see cref="GazeShiftPlan" />. Because each rung is handed a <i>remainder</i>
    ///         rather than an independently-computed fraction, the contributions always sum to
    ///         the shift, which is the invariant that stops one actuator backing off and another
    ///         silently saturating.
    ///     </para>
    ///     <para>
    ///         <b>Why the feet decide on the residual.</b> A fixed "turn the body past N degrees"
    ///         tripwire cannot know whether the neck is comfortable: at 60° it fires for a
    ///         character whose head could have taken it easily, and holds off for one already
    ///         pinned at its limit. Measuring what is left after the head and torso have taken
    ///         their share asks the question that actually matters — is anything still unmet?
    ///     </para>
    /// </remarks>
    internal static class GazeActuatorLadder
    {
        /// <summary>
        ///     Width (degrees) of the band over which a rung fades in around its entry angle.
        ///     A hard cut-in reads as the head snapping into service the instant a target
        ///     crosses an invisible line.
        /// </summary>
        private const float EntryBlendDegrees = 10f;

        // A rung's onset used to ramp its participation in over 0.08 s rather than switching it
        // on, because the stage downstream was a rate limiter and a step in its goal came out as
        // a step in the pose. That is no longer true — the actuator turns a step into a movement
        // with a duration of its own — and once it is, the ramp is actively harmful: the share
        // and the movement would both be shaping the same 0.2 s of time, which is two opinions
        // about one movement, the exact failure this ladder exists to prevent. The onset is now
        // purely a gate: it says WHEN a rung joins, and the actuator owns everything about how.

        /// <summary>Soft-limit knee, shared with the rest of the solver chain.</summary>
        private const float SoftLimitFraction = 0.85f;

        /// <summary>
        ///     Divides <paramref name="measurement" /> across the ladder.
        /// </summary>
        /// <param name="measurement">This frame's required shift.</param>
        /// <param name="capacity">What the rig can contribute and is willing to.</param>
        /// <param name="tuning">Entry angles and onsets.</param>
        /// <param name="shiftAge">Seconds since this shift began — drives the onset cascade.</param>
        /// <param name="engagement">0–1 commitment to the target.</param>
        /// <param name="orbitPressure">
        ///     0–1 from <see cref="GazeComfortModel" />: how hard eyes held off-centre are
        ///     asking the head to take over. Raises the head's share toward its full range so a
        ///     shift the head was only half willing to make gets finished, and the eyes come
        ///     back to centre.
        /// </param>
        /// <param name="comfortPressure">
        ///     0–1 from <see cref="GazeComfortModel" />: how hard a held neck turn is asking for
        ///     the feet. At full pressure the body turns whatever the leftover angle is.
        /// </param>
        public static GazeShiftPlan Solve(
            in GazeShiftMeasurement measurement,
            in GazeLadderCapacity capacity,
            in GazeLadderTuning tuning,
            float shiftAge,
            float engagement,
            float orbitPressure = 0f,
            float comfortPressure = 0f)
        {
            if (!measurement.IsValid || engagement <= 0.0001f) return GazeShiftPlan.Idle;

            float amplitude = measurement.Amplitude;
            float clampedEngagement = Mathf.Clamp01(engagement);

            // ---- Head rung -------------------------------------------------------------
            // Orbit return: a character whose personality only half-commits its head still
            // finishes the job when the eyes have been stuck at the corner of the socket for a
            // second or two. The pressure interpolates willingness toward 1 rather than adding
            // to it, so it can never drive the head past the range the rig allows.
            float willingness = Mathf.Lerp(
                Mathf.Clamp01(capacity.HeadWillingness), 1f, Mathf.Clamp01(orbitPressure));

            // Whether the head is taking part in this look, kept separate from how much of it
            // it takes. The hand-over from idle life is driven by the first question alone —
            // see GazeShiftPlan.HeadRecruitment.
            float headEntry = EntryEase(amplitude, tuning.HeadEntryDegrees);
            float headOnset = OnsetEase(shiftAge, tuning.HeadOnsetSeconds);
            float headRecruitment = headEntry * headOnset;

            // The head is a rung of this shift whose turn has not come yet — as opposed to one
            // that is not a rung of it at all. Only the first is an onset gap for idle life to
            // bridge; see GazeShiftPlan.HeadOnsetPending.
            bool headOnsetPending = headEntry > 0.0001f && headOnset <= 0.0001f;

            float headParticipation = headRecruitment * clampedEngagement * willingness;

            // A rung must not take a share that only exists because the rung below it is
            // blocked. While the feet cannot help — something else owns the character's facing,
            // most often because it is still walking — the head is capped at what the neck can
            // hold comfortably instead of its anatomical limit. Without this, a character
            // finishing its walk cranes its neck to its full range at whoever it is about to
            // face and holds it there until the body is free, which reads as trying to look at
            // you while waiting to stop. It keeps facing where it is going and uses its eyes,
            // then turns properly once the feet are available.
            float headYawCap = capacity.FeetAvailable || capacity.HeadComfortYaw <= 0f
                ? capacity.HeadYaw
                : Mathf.Min(capacity.HeadYaw, capacity.HeadComfortYaw);

            float headYaw = GazeSolverMath.SoftClamp(
                measurement.RequiredYaw * headParticipation, headYawCap, SoftLimitFraction);
            float headPitch = GazeSolverMath.SoftClamp(
                measurement.RequiredPitch * headParticipation, capacity.HeadPitch, SoftLimitFraction);

            // ---- Torso rung: whatever the head could not take --------------------------
            float torsoYaw = 0f;
            float torsoPitch = 0f;
            if (capacity.TorsoAvailable)
            {
                float torsoParticipation =
                    EntryEase(amplitude, tuning.TorsoEntryDegrees) *
                    OnsetEase(shiftAge, tuning.TorsoOnsetSeconds) *
                    clampedEngagement;

                torsoYaw = GazeSolverMath.SoftClamp(
                    (measurement.RequiredYaw - headYaw) * torsoParticipation,
                    capacity.TorsoYaw, SoftLimitFraction);
                torsoPitch = GazeSolverMath.SoftClamp(
                    (measurement.RequiredPitch - headPitch) * torsoParticipation,
                    capacity.TorsoPitch, SoftLimitFraction);
            }

            // ---- Feet rung: whatever is still unmet in yaw ------------------------------
            float residualYaw = measurement.RequiredYaw - headYaw - torsoYaw;

            // Comfort return: the feet turn either because something is still unmet, or
            // because the neck has been held turned long enough to want relief even though
            // nothing is unmet. The second reason is why people turn to face someone they are
            // already looking at, and no fixed angle threshold can express it.
            bool residualUnmet = Mathf.Abs(residualYaw) > tuning.FeetEntryDegrees;
            bool neckWantsRelief = comfortPressure >= 1f;
            bool wantsFeet =
                capacity.FeetAvailable &&
                (residualUnmet || neckWantsRelief) &&
                shiftAge >= tuning.FeetOnsetSeconds;

            return new GazeShiftPlan(
                headYaw, headPitch, torsoYaw, torsoPitch, residualYaw, wantsFeet,
                ResolveDepth(amplitude, tuning, headParticipation, torsoYaw, wantsFeet),
                headRecruitment,
                headOnsetPending);
        }

        /// <summary>
        ///     0 below a rung's entry angle, 1 above it, smoothstepped across
        ///     <see cref="EntryBlendDegrees" /> centred on the entry so the rung fades in.
        /// </summary>
        internal static float EntryEase(float amplitude, float entryDegrees)
        {
            float half = EntryBlendDegrees * 0.5f;
            return GazeSolverMath.RecruitmentEase(
                amplitude, entryDegrees - half, entryDegrees + half);
        }

        /// <summary>
        ///     0 before a rung's onset elapses, 1 after. This is the cascade: one clock, started
        ///     when the shift started, read at a different offset by each rung — not three
        ///     independent hold timers that can stack into a freeze.
        /// </summary>
        /// <remarks>
        ///     A gate rather than a ramp, deliberately — see the note where the ramp used to be
        ///     declared. The step this produces is not a discontinuity in the pose: it is a
        ///     discontinuity in the GOAL, which is what a decision to move looks like, and
        ///     turning it into a movement is the actuator's job.
        /// </remarks>
        internal static float OnsetEase(float shiftAge, float onsetSeconds) =>
            onsetSeconds <= 0f || shiftAge >= onsetSeconds ? 1f : 0f;

        private static GazeLadderDepth ResolveDepth(
            float amplitude,
            in GazeLadderTuning tuning,
            float headParticipation,
            float torsoYaw,
            bool wantsFeet)
        {
            if (wantsFeet) return GazeLadderDepth.Feet;
            if (Mathf.Abs(torsoYaw) > 0.01f) return GazeLadderDepth.Torso;
            if (headParticipation > 0.01f) return GazeLadderDepth.Head;
            return amplitude > 0.01f ? GazeLadderDepth.Eyes : GazeLadderDepth.Idle;
        }
    }
}
