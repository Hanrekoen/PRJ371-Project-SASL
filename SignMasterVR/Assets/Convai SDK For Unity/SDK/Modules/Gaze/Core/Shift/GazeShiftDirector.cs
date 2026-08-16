using Convai.Modules.Gaze.Data;
using UnityEngine;

namespace Convai.Modules.Gaze.Core.Shift
{
    /// <summary>
    ///     Owns the gaze shift as a single event: when it started, and therefore how far through
    ///     the onset cascade each rung of the actuator ladder is.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This is the type whose absence produced the coordination defects. Before it, the
    ///         head solver, the eye solver and the body-turn director each decided their own
    ///         participation from their own threshold and their own timer, and nothing checked
    ///         that the three answers added up to the shift being executed — so a rung backing
    ///         off (body-turn relief) left a gap that only the eyes' clamp absorbed.
    ///     </para>
    ///     <para>
    ///         All it holds is the shift clock. The distribution rule itself is stateless and
    ///         lives in <see cref="GazeActuatorLadder" />, which keeps the interesting logic
    ///         testable without a frame loop.
    ///     </para>
    /// </remarks>
    internal sealed class GazeShiftDirector
    {
        /// <summary>
        ///     Settle time (seconds) for a change in how strongly an <i>unchanged</i> look is
        ///     held — see <see cref="SettleAmplitude" />.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Deliberately longer than the duration law's own base
        ///         (<c>headTurnBaseSeconds</c>, 0.45 s), and that ordering is the requirement, not
        ///         the number. A re-weighting must read as the character relaxing its hold on a
        ///         look it is still making; if it settles faster than the movement it is not, the
        ///         easing has produced something sharper than the thing it was introduced to
        ///         avoid. Easing it at <c>PolicyBlendSpeed</c> — the rate the policy engine uses
        ///         for gains, 0.2 s — would do exactly that.
        ///     </para>
        ///     <para>
        ///         Not a profile field: it is the one rate in the chain that exists to be slower
        ///         than every authorable rate around it, so exposing it would mainly offer authors
        ///         a way to invert that relationship.
        ///     </para>
        /// </remarks>
        private const float ReweightSettleSeconds = 0.9f;

        private readonly GazeComfortModel _comfort = new();
        private int _generation = int.MinValue;
        private float _shiftAge;

        // The ladder's two amplitude inputs, held across frames so a change WITHIN one look can
        // be eased while a change that IS a new look still arrives as a step. See SettleAmplitude.
        private float _engagement;
        private float _headContribution;
        private float _engagementVelocity;
        private float _headContributionVelocity;
        private bool _amplitudeInitialized;

        /// <summary>Seconds since the current shift began. Diagnostics, and the ladder's input.</summary>
        public float ShiftAge => _shiftAge;

        /// <summary>Deepest rung the last plan recruited.</summary>
        public GazeLadderDepth Depth { get; private set; } = GazeLadderDepth.Idle;

        /// <summary>The plan produced by the last <see cref="Plan" /> call.</summary>
        public GazeShiftPlan Current { get; private set; } = GazeShiftPlan.Idle;

        /// <summary>How hard held-off-centre eyes are currently asking the head to take over (0–1).</summary>
        public float OrbitPressure => _comfort.OrbitPressure;

        /// <summary>How hard a held neck turn is currently asking for the feet (0–1).</summary>
        public float ComfortPressure => _comfort.ComfortPressure;

        public void Reset()
        {
            _generation = int.MinValue;
            _shiftAge = 0f;
            _engagement = 0f;
            _headContribution = 0f;
            _engagementVelocity = 0f;
            _headContributionVelocity = 0f;
            _amplitudeInitialized = false;
            _comfort.Reset();
            Depth = GazeLadderDepth.Idle;
            Current = GazeShiftPlan.Idle;
        }

        /// <summary>
        ///     Advances the shift clock and divides this frame's requirement across the ladder.
        /// </summary>
        /// <param name="measurement">The shift still required, measured once from the rig.</param>
        /// <param name="profile">Tuning source.</param>
        /// <param name="engagement">0–1 commitment to the target.</param>
        /// <param name="headContribution">0–1 head willingness from the conversation state policy.</param>
        /// <param name="torsoAvailable">Whether the rig has a torso this character is allowed to use.</param>
        /// <param name="feetAvailable">
        ///     Whether anything else currently owns the character's facing. False while walking a
        ///     path: two systems writing yaw at once is not a coordination problem the ladder can
        ///     solve, so it stands the rung down rather than competing.
        /// </param>
        /// <param name="generationId">Changes when the gaze re-targets, which starts a new shift.</param>
        /// <param name="achievedEyeEccentricityDegrees">
        ///     How far the eyes actually ended up from centre last frame. Fed back in so a pose
        ///     the ladder produced but the body finds uncomfortable keeps evolving — see
        ///     <see cref="GazeComfortModel" />.
        /// </param>
        /// <param name="achievedHeadYawDegrees">Head yaw actually held last frame.</param>
        /// <param name="continuesPreviousShift">
        ///     True when the new target is the same movement continued rather than a fresh
        ///     decision — the hand-off from the path a walking character is watching to whatever
        ///     is at the end of it. Arriving somewhere and settling onto what you came for is
        ///     one movement, not two, so the cascade keeps running instead of restarting and
        ///     freezing the head for another onset.
        /// </param>
        /// <param name="deltaTime">Tick delta.</param>
        public GazeShiftPlan Plan(
            in GazeShiftMeasurement measurement,
            ConvaiGazeProfile profile,
            float engagement,
            float headContribution,
            bool torsoAvailable,
            bool feetAvailable,
            int generationId,
            float deltaTime,
            float achievedEyeEccentricityDegrees = 0f,
            float achievedHeadYawDegrees = 0f,
            bool continuesPreviousShift = false)
        {
            if (profile == null)
            {
                Current = GazeShiftPlan.Idle;
                Depth = GazeLadderDepth.Idle;
                return Current;
            }

            // A re-target is a new shift, so the cascade restarts. Note this is the ONLY clock
            // in the chain now: the head no longer has a latency timer of its own and the body
            // turn no longer has a hysteresis hold, which is what used to let three independent
            // waits stack into a visible freeze on arrival.
            bool newLook = generationId != _generation;
            if (newLook)
            {
                _generation = generationId;
                if (!continuesPreviousShift) _shiftAge = 0f;
                else _shiftAge += Mathf.Max(0f, deltaTime);
            }
            else
            {
                _shiftAge += Mathf.Max(0f, deltaTime);
            }

            // The amplitude the ladder divides — stepped on a new look, eased within one.
            SettleAmplitude(newLook, engagement, headContribution, deltaTime);
            float ladderEngagement = _engagement;
            float ladderHeadContribution = _headContribution;

            var capacity = new GazeLadderCapacity(
                profile.MaxHeadYawDegrees,
                profile.MaxHeadPitchDegrees,
                profile.MaxTorsoYawDegrees,
                profile.MaxTorsoPitchDegrees,
                ladderHeadContribution,
                profile.HeadComfortYawDegrees,
                torsoAvailable && profile.EnableTorsoRecruitment,
                feetAvailable && profile.EnableBodyTurn);

            var tuning = new GazeLadderTuning(
                profile.HeadEntryDegrees,
                profile.TorsoEntryDegrees,
                profile.FeetEntryDegrees,
                profile.HeadOnsetSeconds,
                profile.TorsoOnsetSeconds,
                profile.FeetOnsetSeconds);

            // Comfort runs on what was ACHIEVED, not on what was planned: the question is
            // whether the pose the character is actually holding is tiring, which only the
            // previous frame's outcome can answer.
            _comfort.Tick(
                achievedEyeEccentricityDegrees,
                achievedHeadYawDegrees,
                profile.EyeComfortDegrees,
                profile.HeadComfortYawDegrees,
                ladderEngagement > 0.0001f,
                deltaTime);

            Current = GazeActuatorLadder.Solve(
                in measurement, in capacity, in tuning, _shiftAge, ladderEngagement,
                _comfort.OrbitPressure, _comfort.ComfortPressure);
            Depth = Current.Depth;
            return Current;
        }

        /// <summary>
        ///     Advances the two values that set the ladder's amplitude — how strongly the
        ///     character is committed to this look, and how much of it the head is willing to
        ///     take — stepping them on a new look and easing them within one.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>Why the two cases differ.</b> The actuator turns a step in its goal into a
        ///         movement: it picks a duration from the amplitude and runs a velocity profile.
        ///         That is exactly right when the goal stepped because the character decided to
        ///         look somewhere else — the step IS the decision, and the ladder must see the
        ///         full destination on the first frame or the movement is planned against the
        ///         wrong amplitude.
        ///     </para>
        ///     <para>
        ///         It is exactly wrong when the goal stepped because the <i>weighting</i> of an
        ///         unchanged look moved. A conversation-state edge, the floor-yield engagement
        ///         pin arming and expiring, the target-loss search's floor, an emotion modifier —
        ///         none of them is a decision to look elsewhere, and the target has not moved. Fed
        ///         straight through, each one made the actuator plan a full, correctly shaped
        ///         movement to re-aim a look that never changed: on the shipped table, Speaking
        ///         (1.0 / 0.85) to Settling (0.6 / 0.6) drops head participation from 0.85 to 0.36
        ///         in one frame, so the head deliberately turns away from a target the eyes are
        ///         still holding, and does it again when the pin expires 0.8 s later.
        ///     </para>
        ///     <para>
        ///         The generation id is what tells the two apart, and it is the same signal the
        ///         shift clock above already trusts to mean "this is a different look".
        ///     </para>
        ///     <para>
        ///         Second order (critically damped) rather than the exponential the policy engine
        ///         uses for gains: the eased value is a movement's goal, and it is consumed by a
        ///         tracking filter that is transparent to in-budget motion, so whatever shape the
        ///         goal has is the shape that reaches the bone. An exponential starts at its peak
        ///         rate, which puts a velocity step on the neck; this starts and ends at rest.
        ///     </para>
        /// </remarks>
        private void SettleAmplitude(
            bool newLook,
            float engagement,
            float headContribution,
            float deltaTime)
        {
            engagement = Mathf.Clamp01(engagement);
            headContribution = Mathf.Clamp01(headContribution);

            if (!_amplitudeInitialized || newLook)
            {
                _amplitudeInitialized = true;
                _engagement = engagement;
                _headContribution = headContribution;
                _engagementVelocity = 0f;
                _headContributionVelocity = 0f;
                return;
            }

            float dt = Mathf.Max(0f, deltaTime);
            _engagement = Mathf.SmoothDamp(
                _engagement, engagement, ref _engagementVelocity,
                ReweightSettleSeconds, Mathf.Infinity, dt);
            _headContribution = Mathf.SmoothDamp(
                _headContribution, headContribution, ref _headContributionVelocity,
                ReweightSettleSeconds, Mathf.Infinity, dt);
        }
    }
}
