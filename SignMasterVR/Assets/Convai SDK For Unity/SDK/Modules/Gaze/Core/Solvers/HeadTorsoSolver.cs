using Convai.Modules.Gaze.Core.Shift;
using Convai.Modules.Gaze.Data;
using Convai.Runtime.Animation.ProceduralPose;
using UnityEngine;

namespace Convai.Modules.Gaze.Core.Solvers
{
    /// <summary>Per-frame input for <see cref="HeadTorsoSolver.Solve" />.</summary>
    internal struct HeadTorsoSolveInput
    {
        public GazeChainCalibration Chain;
        public ConvaiGazeProfile Profile;
        public float DeltaTime;

        /// <summary>
        ///     The character's shared pose compositor, when it has one. Every bone this solver
        ///     writes — chest, upper chest, neck, head — routes through it, so one guard owns
        ///     the restore protocol for the whole set. Null on a character with no Body
        ///     Language, where the solver falls back to its own private guard.
        /// </summary>
        public ProceduralPoseCompositor PoseSink;

        /// <summary>World point being gazed at (valid when <see cref="HasTarget" />).</summary>
        public Vector3 TargetPoint;
        public bool HasTarget;

        /// <summary>
        ///     This frame's shift requirement, measured once by
        ///     <c>GazeChainCalibration.TryMeasureShift</c>. Required whenever
        ///     <see cref="HasTarget" /> is set.
        /// </summary>
        public GazeShiftMeasurement Measurement;

        /// <summary>
        ///     This actuator's allocated share of the shift, from the actuator ladder. The
        ///     solver executes it; it no longer decides it.
        /// </summary>
        public GazeShiftPlan Plan;

        /// <summary>Effective engagement 0–1 (already includes commitment).</summary>
        public float Engagement;

        /// <summary>Ambient exploration angles (yaw/pitch degrees) when no target is engaged.</summary>
        public Vector2 AmbientAngles;
        public bool AmbientActive;

        /// <summary>
        ///     Whether idle life still has a fixation to hand over: the look is not yet fully
        ///     taken up (or is being released) and ambient exploration is enabled.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         While set, the head keeps holding its ambient fixation until it actually joins
        ///         the look (<c>Plan.HeadOnsetPending</c>), and takes the fixation back when it
        ///         stops taking part. False — the default, and what a fully committed look
        ///         supplies — leaves the allocated share untouched. The caller is responsible for
        ///         only setting it while idle life genuinely still holds a fixation to hand back
        ///         (<c>AmbientExplorationDirector.HasResumableFixation</c>): once the resume
        ///         window has cleared the angles, "hand the head back" would mean "face front".
        ///     </para>
        ///     <para>
        ///         It exists because the two sources of the head's share are chosen by a boolean
        ///         that flips a whole frame before the ladder has any share to hand over: on the
        ///         first frame of a look the head's onset has not elapsed, so the allocated share
        ///         is zero while the idle fixation the head was holding is dropped. The head
        ///         therefore returned to centre for the length of the onset gap and then turned
        ///         out to the target — one look executed as two movements in opposite directions,
        ///         which is what an idle curiosity glance did every time it fired.
        ///     </para>
        ///     <para>
        ///         Deliberately a gate and not a crossfade. Fading the two shares over the
        ///         commitment ramp removes the reversal but replaces it with something worse: the
        ///         goal becomes continuous, so the movement detector never fires, and a
        ///         continuous goal is <i>tracked</i> rather than shaped — the head then covers the
        ///         whole turn at the ramp's speed with no duration law and no velocity profile,
        ///         which reads far faster and harsher than the movement it replaced. The goal must
        ///         step exactly once, from the fixation to the share, and let the lane make a
        ///         movement out of it.
        ///     </para>
        /// </remarks>
        public bool AmbientHandover;

        /// <summary>Aversion beat offset (yaw/pitch degrees); the head carries half of it.</summary>
        public Vector2 AversionOffset;

        /// <summary>
        ///     Head gesture offset (yaw/pitch degrees, pitch-dominant) — e.g. listening
        ///     backchannel nods. Layered onto the bones AFTER the smoothing springs: the
        ///     envelope is authored motion with its own attack/decay, and chasing it through
        ///     the spring (or the stability band) would low-pass a sub-second nod to mush.
        ///     Producers must emit a continuous signal (the springs no longer smooth it).
        /// </summary>
        public Vector2 GestureOffset;

        /// <summary>
        ///     Additive head-gesture roll (tilt axis) in degrees — e.g. an external head-tilt
        ///     program. Gesture-only: there is no aim roll to compose against (the target-aim
        ///     path never produces roll), so this lands on the Head bone alone, soft-clamped
        ///     against a conservative internal limit (see <see cref="HeadTorsoSolver" />).
        ///     Zero by default; a zero value must be a complete no-op (see the solver's
        ///     bit-identity remarks).
        /// </summary>
        public float GestureRollDegrees;

        /// <summary>
        ///     True while a body reorientation (animated or procedural) is in flight: the
        ///     head/torso offsets are relieved so the neck rides the turn instead of staying
        ///     pinned at its limit.
        /// </summary>
        public bool BodyTurnActive;

        /// <summary>
        ///     What kind of movement this is, which decides how long it takes. Defaults to
        ///     <see cref="GazeMovementUrgency.Relaxed" /> because that is the zero value and idle
        ///     life is the case with no target to classify — a caller that never sets this gets
        ///     unhurried movement rather than an alert character, which is the safer default to
        ///     be wrong in.
        /// </summary>
        public GazeMovementUrgency Urgency;
    }

    /// <summary>
    ///     Anatomical head/neck/torso stage of the gaze chain: executes the share of a gaze
    ///     shift the actuator ladder allocated to it, cancels the animation's own head
    ///     deviation, smooths under an angular speed clamp, and layers the result on top of
    ///     the Animator's pose as world-axis swing deltas.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         An actuator, not a decision-maker. How much of the shift the head takes, and
    ///         when it starts taking it, belong to <see cref="Shift.GazeShiftDirector" /> and
    ///         <see cref="Shift.GazeActuatorLadder" />. This type used to decide both for
    ///         itself, from its own recruitment threshold and its own latency timer, while the
    ///         eye solver and the body-turn director decided theirs — and nothing checked the
    ///         three answers added up to one shift.
    ///     </para>
    ///     <para>
    ///         Because the deltas are recomputed from the animated pose every frame, idle
    ///         animation personality (head bobs, weight shifts) survives under the gaze and no
    ///         bone-ownership tracking is needed: when the applied angles reach zero the solver
    ///         simply stops writing.
    ///     </para>
    /// </remarks>
    internal sealed class HeadTorsoSolver
    {
        /// <summary>
        ///     Settle time (seconds) of the body-turn relief blend. The relief factor eases in
        ///     when a turn starts and back out when it completes — a binary switch here stepped
        ///     the head goal the instant a turn ended and read as a whip.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>Second order, and slower than it was.</b> Easing the factor exponentially
        ///         removed the step in the goal's POSITION and left one in its VELOCITY: an
        ///         exponential leaves at its peak rate, so the instant a turn began, the relief
        ///         factor acquired its full slew in one frame. That factor multiplies the head's
        ///         whole allocated share, so on a 45° look the goal picked up about 160 °/s of
        ///         velocity from a standing start. The tracking lane is transparent to in-budget
        ///         motion and therefore reproduces whatever the goal does, so it chased that with
        ///         everything it had: measured at a clean ±1500 °/s² — the acceleration envelope,
        ///         exactly — ramping to a 212 °/s peak and braking again. Bang-bang, which is the
        ///         harshest motion the caps allow and the precise shape this module's two-lane
        ///         actuator exists to never produce.
        ///     </para>
        ///     <para>
        ///         So the blend is critically damped (leaves and arrives at rest, no velocity step
        ///         at either end) and its timescale is comparable to the duration law's own base
        ///         rather than a third of it. Relief moves the head by <c>share × (1 - relief)</c>
        ///         — 27° on that same 45° look — and a movement that size is not something a neck
        ///         does in a fifth of a second. Doing it faster than <c>HeadTurnBaseSeconds</c>
        ///         meant the relief blend was quietly bypassing the duration law for one of the
        ///         largest head movements the module makes.
        ///     </para>
        /// </remarks>
        private const float ReliefSettleSeconds = 0.45f;

        /// <summary>
        ///     Conservative internal roll limit (degrees) for the gesture-only tilt axis. Roll
        ///     never comes from the aim solve (there is no "roll toward a target"), only from a
        ///     head-gesture producer, so this is a hardcoded internal cap rather than a profile
        ///     field — a tilt program is expected to stay well inside it.
        /// </summary>
        private const float MaxGestureRollDegrees = 10f;

        /// <summary>
        ///     Duration multiplier for a movement the character did not choose to make — a startle
        ///     beat, or re-acquiring a target after a cut. Not a profile field: a reflex is a
        ///     reflex, and a character whose startle response is authored to be leisurely is
        ///     describing a different thing than this scale is for.
        /// </summary>
        private const float UrgentTempoScale = 0.75f;

        /// <summary>
        ///     How much further the allocated share must jump to interrupt a movement that is
        ///     already running, as a multiple of the profile's trigger. See DetectMovement.
        /// </summary>
        private const float RetriggerHysteresis = 3f;

        /// <summary>
        ///     Response rate (per second) of the stabilization reflex's gain — about a fifth of a
        ///     second. Fast enough that engaging and disengaging still read as decisions, slow
        ///     enough that a policy value stepping can never arrive as a pose step.
        /// </summary>
        private const float StabilizationGainSharpness = 5f;

        /// <summary>
        ///     Overlapping action, as a redistribution of the neck/head split rather than an
        ///     offset on either bone.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         A real neck does not rotate as one rigid piece: a turn travels up it, so the
        ///         neck leads, the head trails, and when the movement stops the head carries on a
        ///         degree or two and settles back. Its absence is the most recognisable tell of
        ///         procedural head motion — smooth, correctly timed, and unmistakably a machine.
        ///     </para>
        ///     <para>
        ///         The trick that makes this safe is where it is applied. Adding a lag offset to
        ///         the head bone would change where the character is looking, and the whole point
        ///         of the actuator ladder is that the contributions reconstruct the allocated
        ///         look exactly. Moving the SPLIT instead leaves the composed aim swing
        ///         bit-identical and only changes how it is divided between the two bones, so
        ///         conservation is untouched by construction and there is nothing to keep in sync.
        ///     </para>
        ///     <para>
        ///         Driven by the aim's own speed through an under-damped second order system, so
        ///         the same mechanism produces both halves of the effect: it builds while the head
        ///         is moving (neck leads) and rings down once it stops (head overshoots, then
        ///         settles). Two features, one signal, no phase bookkeeping.
        ///     </para>
        /// </remarks>
        private static class ChainLag
        {
            /// <summary>Largest share redistribution at full follow-through, in share units.</summary>
            public const float MaxShare = 0.15f;

            /// <summary>
            ///     Aim speed (deg/s) that drives the redistribution to its maximum. Calibrated
            ///     against the speeds conversational movement actually reaches — roughly 55 °/s
            ///     for a 20° look and 85 °/s for a 40° one — so an ordinary turn produces a
            ///     visible amount of flex rather than a fraction of one. A reference set for
            ///     maximal-effort movement would leave the whole effect dormant in normal use.
            /// </summary>
            public const float ReferenceSpeed = 90f;

            /// <summary>Undamped natural frequency (rad/s) — sets the settle's period.</summary>
            public const float Frequency = 18f;

            /// <summary>Damping ratio. Below 1 so the settle overshoots once rather than creeping.</summary>
            public const float DampingRatio = 0.35f;

            public const float Stiffness = Frequency * Frequency;
            public const float Damping = 2f * DampingRatio * Frequency;

            /// <summary>Bounds on the redistribution, so the split can never invert or saturate.</summary>
            public const float MinClamp = -0.2f;
            public const float MaxClamp = 0.35f;
        }

        /// <summary>
        ///     Acceleration ceilings for the head and chest chains — a safety envelope, not a feel
        ///     setting.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         These are deliberately NOT profile fields, and that is a change of role rather
        ///         than an oversight. They used to be the thing that shaped a movement, because a
        ///         rate limiter running against its caps IS the movement — which made them
        ///         feel-critical constants that nobody could author. Now the shape comes from the
        ///         duration law on the profile and these only stop a movement that was planned
        ///         impossibly (a goal that jumped a long way with almost no time left) from
        ///         producing a rate no body could make.
        ///     </para>
        ///     <para>
        ///         At the shipped durations they are not reached. They CAN be, by authoring a turn
        ///         time far shorter than the movement's size warrants — the profile's floor of
        ///         0.05 s asks for accelerations two orders of magnitude past these — in which case
        ///         the movement degrades back toward the time-optimal shape this rework exists to
        ///         remove. <see cref="BallisticMotor.EnvelopeEngaged" /> reports the frame it
        ///         happens; nothing acts on it at runtime, and it is asserted in the primitive's
        ///         own tests rather than surfaced as a diagnostic.
        ///     </para>
        /// </remarks>
        private static class SafetyEnvelope
        {
            public const float HeadMaxAccel = 1500f;
            public const float TorsoMaxAccel = 600f;
        }

        private readonly AnimatedPoseWriteGuard _writeGuard = new();
        private ProceduralPoseCompositor _poseSink;

        // The two lanes. A channel is driven by exactly one of them at a time: the ballistic lane
        // while a movement is in flight, the tracking filter the rest of the time. See ExecuteLane.
        private BallisticMotor _headYawShift;
        private BallisticMotor _headPitchShift;
        private BallisticMotor _torsoYawShift;
        private BallisticMotor _torsoPitchShift;
        private MotorFilter _headYawMotor;
        private MotorFilter _headPitchMotor;
        private MotorFilter _torsoYawMotor;
        private MotorFilter _torsoPitchMotor;

        // Last frame's allocated share, per chain — the signal a new movement is detected on. It
        // is deliberately the RAW share, before relief, the stability band and the stabilization
        // reflex: those are all continuous corrections to a movement, and letting them reach the
        // detector would have the animation's own head-bob restart the movement every frame.
        private Vector2 _lastHeadShare;
        private Vector2 _lastTorsoShare;
        private bool _hasLastShare;

        // Overlapping action: how far the neck/head split is currently displaced from the
        // authored one, and the second-order state that carries it. See ChainLag.
        private float _chainLag;
        private float _chainLagVelocity;
        private Vector2 _previousAim;
        private bool _hasPreviousAim;

        // The animated-deviation reflex's two factors, both of which must be continuous: an eased
        // gain, and the last deviation actually measured, held so it can be faded out rather than
        // dropped when the measurement stops being available.
        private float _stabilizationGain;
        private Vector2 _lastAnimatedDeviation;

        private float _headYaw;
        private float _headPitch;
        private float _torsoYaw;
        private float _torsoPitch;
        private float _heldGoalYaw;
        private float _heldGoalPitch;
        private float _reliefBlend = 1f;
        private float _reliefVelocity;
        private float _appliedHeadYaw;
        private float _appliedHeadPitch;
        private float _appliedHeadRoll;

        /// <summary>Head-chain contribution (degrees) actually written this frame: spring-smoothed aim + gesture channel.</summary>
        public Vector2 HeadAngles => new(_appliedHeadYaw, _appliedHeadPitch);

        /// <summary>Applied gesture roll (degrees) written to the Head bone this frame; zero outside an active roll gesture.</summary>
        public float HeadRollDegrees => _appliedHeadRoll;

        /// <summary>Solved torso contribution (degrees) after smoothing.</summary>
        public Vector2 TorsoAngles => new(_torsoYaw, _torsoPitch);

        /// <summary>
        ///     Total yaw error (degrees, signed) between the root forward and the target
        ///     this frame — the reorientation director's input.
        /// </summary>
        public float TargetYawError { get; private set; }

        /// <summary>
        ///     Total pitch error (degrees, signed) from the eye line to the target this frame.
        ///     The reorientation director has no use for it (feet only turn in yaw); it exists
        ///     so the coordination invariant — eyes + head + torso + feet sum to the required
        ///     shift — is measurable on both axes rather than only the one the body turns on.
        /// </summary>
        public float TargetPitchError { get; private set; }

        public void Reset()
        {
            // Unwind a still-applied write so a disabled gaze leaves the pose as found.
            _writeGuard.RestoreStaleWrites();
            _headYaw = 0f;
            _headPitch = 0f;
            _torsoYaw = 0f;
            _torsoPitch = 0f;
            _headYawShift.Reset();
            _headPitchShift.Reset();
            _torsoYawShift.Reset();
            _torsoPitchShift.Reset();
            _headYawMotor.Reset();
            _headPitchMotor.Reset();
            _torsoYawMotor.Reset();
            _torsoPitchMotor.Reset();
            _lastHeadShare = Vector2.zero;
            _lastTorsoShare = Vector2.zero;
            _hasLastShare = false;
            _chainLag = 0f;
            _chainLagVelocity = 0f;
            _previousAim = Vector2.zero;
            _hasPreviousAim = false;
            _stabilizationGain = 0f;
            _lastAnimatedDeviation = Vector2.zero;
            _heldGoalYaw = 0f;
            _heldGoalPitch = 0f;
            _reliefBlend = 1f;
            _reliefVelocity = 0f;
            _appliedHeadYaw = 0f;
            _appliedHeadPitch = 0f;
            _appliedHeadRoll = 0f;
            TargetYawError = 0f;
            TargetPitchError = 0f;
        }

        public void Solve(in HeadTorsoSolveInput input)
        {
            // Stashed for Apply (below), which has no access to this frame's input struct.
            _poseSink = input.PoseSink;

            // With no animation source re-posing the skeleton this frame (Body Animation
            // disabled, Animator without a controller), last frame's swing deltas are
            // still on the bones — unwind them first so goals are computed against the
            // true underlying pose and the deltas never integrate into a runaway spin.
            _writeGuard.RestoreStaleWrites();

            GazeChainCalibration chain = input.Chain;
            ConvaiGazeProfile profile = input.Profile;
            if (chain == null || profile == null || !chain.HasHeadChain || chain.Root == null) return;

            float dt = input.DeltaTime > 0f ? input.DeltaTime : 1f / 60f;

            float reliefGoal = input.BodyTurnActive ? Mathf.Clamp01(profile.BodyTurnHeadRelief) : 1f;
            _reliefBlend = Mathf.SmoothDamp(
                _reliefBlend, reliefGoal, ref _reliefVelocity, ReliefSettleSeconds, Mathf.Infinity, dt);

            // ---- Decide the movement, then execute it. ----
            //
            // These are two different questions and they used to be one. The share this actuator
            // owns is decided upstream by the ladder; what is decided HERE is whether that share
            // changing constitutes a new movement, and if so what shape and duration that movement
            // has. Conflating them is what let a step in the goal become a step in the output.
            ComputeShares(in input, profile, out Vector2 headShare, out Vector2 torsoShare);

            // Body-turn relief scales what the lanes aim at, but never what the movement detector
            // reads: relief eases continuously, and a movement must not be restarted by the neck
            // relaxing into a turn it is already riding.
            Vector2 headTracked = headShare * _reliefBlend;
            Vector2 torsoTracked = torsoShare * _reliefBlend;

            DetectMovement(headShare, torsoShare, profile, out bool headMoved, out bool torsoMoved);
            float tempo = TempoScale(input.Urgency, profile);

            ExecuteHeadLane(headTracked, headMoved, tempo, profile, dt);
            ExecuteTorsoLane(torsoTracked, torsoMoved, tempo, profile, dt);

            // Overlapping action, driven by the movement the lanes just produced — not by the
            // dressings below it. A nod is authored motion that already has its own shape; using
            // it to drive the chain lag would ring the neck against a signal that is not a turn.
            UpdateChainLag(new Vector2(_headYaw, _headPitch), profile, dt);

            // ---- Everything that is not the movement, composed on top of it. ----
            //
            // Three channels layer onto the executed aim rather than being routed through it,
            // each for its own reason:
            //
            //  · the stabilization reflex, because a reflex that lags the animation it cancels
            //    leaves exactly the residual bow it exists to remove — and because feeding the
            //    animated head-bob into the lanes would restart the movement every frame;
            //  · the aversion beat, because a look-away is a dressing on the shift with its own
            //    envelope, not a bigger shift (routing it through the lanes would also let a
            //    glance recruit a full movement);
            //  · the gesture channel (backchannel nods), because a 0.7 s double-bob is authored
            //    motion whose frequency content sits right where a filter attenuates hardest.
            //
            // Relief scales the voluntary dressings but never the reflex — see ComputeShares.
            // The reflex is immediate; how much of it applies is not.
            //
            // The reflex itself must never lag — it cancels the animation's own head movement, and
            // a lagging canceller leaves exactly the residual bow it exists to remove. That is why
            // it is composed here, outside both lanes. But its GAIN is a policy value, and policy
            // values step: engagement is pinned to 1 by the floor-yield beat and floored at 0.6 by
            // the target-loss search, both on boolean edges, and it moves whenever the dialogue
            // state does. A step in the gain is indistinguishable from a step in the pose — the
            // head jumps by the animated deviation times the change, with nothing in the way,
            // because this term is downstream of everything that shapes motion.
            //
            // So the gain is eased and the reflex is not. Tracking the animation stays frame-exact
            // while the amount of it that reaches the pose can only ever ramp.
            float stabilizationTarget =
                Mathf.Clamp01(input.Engagement) * Mathf.Clamp01(profile.HeadStabilization);
            _stabilizationGain += (stabilizationTarget - _stabilizationGain) *
                                  (1f - Mathf.Exp(-StabilizationGainSharpness * dt));

            // The reflex has two factors and BOTH have to be continuous — easing one and letting
            // the other step just moves the step.
            //
            // The deviation is only measurable while there is a target: the controller hands over
            // a cleared measurement the frame gaze disengages. Read straight, that zeroes the
            // reflex in one frame while the gain is still near 1, and the head — which was being
            // held level against the animation's bow — drops onto that bow instantly. On a talking
            // clip that is a dozen degrees, on every single release.
            //
            // So the last measured deviation is held and faded out by the gain instead. While
            // engaged it is refreshed every frame, which keeps the reflex frame-exact, the whole
            // reason it sits outside the lanes. While disengaging it is stale — and stale is
            // exactly right for a value whose only remaining job is to reach zero smoothly.
            if (input.Measurement.IsValid)
                _lastAnimatedDeviation =
                    new Vector2(input.Measurement.AnimatedYaw, input.Measurement.AnimatedPitch);

            float reflexYaw = -_lastAnimatedDeviation.x * _stabilizationGain;
            float reflexPitch = -_lastAnimatedDeviation.y * _stabilizationGain;

            float dressingYaw = (input.AversionOffset.x * 0.5f + input.GestureOffset.x) * _reliefBlend;
            float dressingPitch = (input.AversionOffset.y * 0.5f + input.GestureOffset.y) * _reliefBlend;

            _appliedHeadYaw = GazeSolverMath.SoftClamp(
                _headYaw + dressingYaw + reflexYaw, profile.MaxHeadYawDegrees, 0.85f);
            _appliedHeadPitch = GazeSolverMath.SoftClamp(
                _headPitch + dressingPitch + reflexPitch, profile.MaxHeadPitchDegrees, 0.85f);

            // Gesture roll (tilt axis): gesture-only, so there is nothing to add it to besides
            // the relief blend — no aim-roll goal exists to spring toward. Branched on non-zero
            // so the zero-gesture path (today's only path) never executes this line at all,
            // which is the bit-identity guarantee: a literal `0f` input can never observably
            // differ from the assignment below evaluating to exactly 0f, but keeping the whole
            // computation out of the executed path removes any doubt and matches the yaw/pitch
            // gesture channel's own convention of only doing work when there is a signal.
            if (input.GestureRollDegrees != 0f)
                _appliedHeadRoll = GazeSolverMath.SoftClamp(
                    input.GestureRollDegrees * _reliefBlend, MaxGestureRollDegrees, 0.85f);
            else
                _appliedHeadRoll = 0f;

            Apply(chain, profile);
        }

        /// <summary>
        ///     This actuator's raw allocated share of the current look — what the body is being
        ///     asked to hold, before anything is done about how it gets there.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         The share is decided by the actuator ladder (<c>GazeShiftDirector</c>), not
        ///         here. This solver used to compute its own recruitment ease, its own latency
        ///         window and its own torso overflow, while the body-turn director computed its
        ///         own threshold and hold from the same error — four opinions about one shift,
        ///         with nothing checking they added up.
        ///     </para>
        ///     <para>
        ///         Deliberately raw: no relief, no stability band, no stabilization reflex, no
        ///         aversion. Every one of those is a continuous correction applied to a movement,
        ///         and this value is what the movement DETECTOR reads — so folding any of them in
        ///         would have a body turn, a camera nudge or the animation's own head-bob read as
        ///         a decision to look somewhere else.
        ///     </para>
        /// </remarks>
        private void ComputeShares(
            in HeadTorsoSolveInput input,
            ConvaiGazeProfile profile,
            out Vector2 headShare,
            out Vector2 torsoShare)
        {
            headShare = Vector2.zero;
            torsoShare = Vector2.zero;
            TargetYawError = 0f;
            TargetPitchError = 0f;

            if (input.HasTarget && input.Engagement > 0.0001f)
            {
                if (!input.Measurement.IsValid) return;

                // Echoed for diagnostics only. The measurement itself is taken once, by the
                // chain calibration, and handed to every stage — see
                // GazeChainCalibration.TryMeasureShift.
                TargetYawError = input.Measurement.RequiredYaw;
                TargetPitchError = input.Measurement.RequiredPitch;

                headShare = new Vector2(input.Plan.HeadYaw, input.Plan.HeadPitch);
                torsoShare = new Vector2(input.Plan.TorsoYaw, input.Plan.TorsoPitch);

                // Hand-over from idle life: until the head joins the look, it goes on holding the
                // fixation it was already on rather than being dropped to a share the ladder has
                // not allocated yet. One step, at the moment the head takes part — see
                // AmbientHandover for why this is a gate and not a fade. The torso is not in it:
                // idle life never recruits the chest, so it has nothing to hand over.
                //
                // Keyed on the ONSET being pending, not on the allocated share being zero. The
                // two are not the same question: a shift below the head's entry angle allocates
                // nothing to the head forever, because the eyes own small looks — and reading
                // that as "the head has not joined yet" handed the head an idle fixation for the
                // length of every commitment dip in a settled conversation, then snapped it back.
                // See GazeShiftPlan.HeadOnsetPending.
                if (input.AmbientHandover && input.Plan.HeadOnsetPending)
                    headShare = AmbientHeadShare(profile, input.AmbientAngles);
                return;
            }

            // Idle life. The ambient director hands over a discrete fixation — it decides to
            // look somewhere, it does not slide there — and that is correct modelling: an
            // intention is not a ramp. Turning that decision into a movement is this stage's
            // job, which is exactly why the step arrives here rather than being smoothed away
            // at the source.
            if (input.AmbientActive)
                headShare = AmbientHeadShare(profile, input.AmbientAngles);

            // No target and no ambient → the share is zero, and returning to neutral is itself
            // a movement rather than a decay.
        }

        /// <summary>
        ///     The head's part of an ambient fixation: the profile's follow fraction of the
        ///     angles the exploration director is holding, soft-clamped to the head's range.
        /// </summary>
        private static Vector2 AmbientHeadShare(ConvaiGazeProfile profile, Vector2 ambientAngles)
        {
            float follow = Mathf.Clamp01(profile.AmbientHeadFollow);
            return new Vector2(
                GazeSolverMath.SoftClamp(
                    ambientAngles.x * follow, profile.MaxHeadYawDegrees, 0.85f),
                GazeSolverMath.SoftClamp(
                    ambientAngles.y * follow, profile.MaxHeadPitchDegrees, 0.85f));
        }

        /// <summary>
        ///     Whether this frame's share represents a decision to look somewhere else, as
        ///     opposed to the current look being adjusted.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         One decision, reported per rung. Both flags are read off the same ladder
        ///         allocation, so head and chest can never disagree about WHICH look is being
        ///         made — but they must be free to disagree about when their own part of it
        ///         starts, because the onset cascade is exactly that disagreement, deliberately
        ///         staged. A single shared flag would have the chest joining at its onset restart
        ///         the head's movement, which was already a third of the way through.
        ///     </para>
        ///     <para>
        ///         The first frame after a bind never counts — there is no previous share to have
        ///         moved away from, and the tracking filters initialise onto their goal.
        ///     </para>
        /// </remarks>
        private void DetectMovement(
            Vector2 headShare,
            Vector2 torsoShare,
            ConvaiGazeProfile profile,
            out bool headMoved,
            out bool torsoMoved)
        {
            if (!_hasLastShare)
            {
                _lastHeadShare = headShare;
                _lastTorsoShare = torsoShare;
                _hasLastShare = true;
                headMoved = false;
                torsoMoved = false;

                // The stability band starts CENTRED on the first share, not at zero.
                //
                // The bind frame is the one frame that produces no movement — there is no previous
                // share to have moved away from — so the channel is initialised rather than driven:
                // the tracking filter's first Step snaps to its goal. But its goal is the held goal,
                // and HoldInBand drags a held goal only by the EXCESS over the band, so starting the
                // band at zero left it parked exactly `HeadStabilityDegrees` short of the share and
                // the filter then initialised onto that. Nothing afterwards closes it: the band is
                // satisfied, so the head sits at a permanent 2.5° bias for as long as the goal holds
                // still, and only ever converges if some later movement happens to run the ballistic
                // lane, which lands exactly and re-centres the band on the way out.
                //
                // That asymmetry is the bug: where the head comes to rest depended on which lane it
                // arrived by. A dead-band is there to reject small CHANGES, not to introduce a
                // standing offset.
                _heldGoalYaw = headShare.x;
                _heldGoalPitch = headShare.y;
                return;
            }

            float trigger = Mathf.Max(0.01f, profile.ShiftTriggerDegrees);

            // Hysteresis while a movement is already running. The share is measured in the
            // character-root frame, so anything that rotates the root — a body turn, most of all —
            // sweeps the share past the trigger every single frame. Re-arming on each of those
            // would restart the movement continuously: it never pops (the restart carries position
            // and velocity) but the movement's shape is thrown away and the lane degrades into a
            // plain lag tracker for the length of the turn, which is the one place the shape is
            // most visible. A genuinely new decision clears the wider bar easily; a root sweeping
            // underneath an existing one does not.
            float headTrigger = _headYawShift.IsActive ? trigger * RetriggerHysteresis : trigger;
            float torsoTrigger = _torsoYawShift.IsActive ? trigger * RetriggerHysteresis : trigger;

            headMoved = (headShare - _lastHeadShare).sqrMagnitude > headTrigger * headTrigger;
            torsoMoved = (torsoShare - _lastTorsoShare).sqrMagnitude > torsoTrigger * torsoTrigger;

            _lastHeadShare = headShare;
            _lastTorsoShare = torsoShare;
        }

        /// <summary>
        ///     Executes the head chain's share: ballistically while a movement is in flight,
        ///     under the tracking filter the rest of the time.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         The two lanes exist because two different signals arrive on this channel. A
        ///         decision to look elsewhere arrives as a step and wants a movement — a duration
        ///         chosen from its size, and a bell-shaped velocity profile. A target drifting,
        ///         or the relief blend easing, arrives continuously and wants no shaping at all,
        ///         only a guarantee of no lag. One filter cannot serve both: a spring gives the
        ///         second a permanent following error, and a rate limiter turns the first into
        ///         the time-optimal trajectory, which is the harshest motion its caps allow.
        ///     </para>
        ///     <para>
        ///         The hand-off is continuous by construction. A movement ends at rest exactly on
        ///         its goal, so the tracking filter is seeded there with no velocity to carry;
        ///         and a movement that begins mid-track takes the filter's current velocity with
        ///         it, so a new decision taken while the head is already moving does not stall it
        ///         first. Both directions matter — the second is what a re-target during a turn
        ///         hits every time.
        ///     </para>
        /// </remarks>
        private void ExecuteHeadLane(
            Vector2 tracked, bool startMovement, float tempo, ConvaiGazeProfile profile, float dt)
        {
            float speed = Mathf.Max(1f, profile.MaxHeadAngularSpeed);
            float skew = Mathf.Clamp01(profile.MovementSkew);

            if (startMovement)
            {
                float distance = Vector2.Distance(new Vector2(_headYaw, _headPitch), tracked);
                float duration = MovementSeconds(
                    distance, profile.HeadTurnBaseSeconds, profile.HeadTurnSecondsPerDegree, tempo);
                if (duration > 0f)
                {
                    // Read before Begin: this is the velocity the channel is carrying INTO the
                    // new movement, and Begin overwrites it.
                    float yawVelocity = LaneVelocity(in _headYawShift, in _headYawMotor);
                    float pitchVelocity = LaneVelocity(in _headPitchShift, in _headPitchMotor);
                    _headYawShift.Begin(_headYaw, yawVelocity, duration);
                    _headPitchShift.Begin(_headPitch, pitchVelocity, duration);
                }
            }

            if (_headYawShift.IsActive)
            {
                _headYaw = _headYawShift.Step(tracked.x, speed, SafetyEnvelope.HeadMaxAccel, skew, dt);
                _headPitch = _headPitchShift.Step(tracked.y, speed, SafetyEnvelope.HeadMaxAccel, skew, dt);

                // Both channels share one duration, so they land on the same frame; the yaw
                // channel speaks for the pair.
                if (!_headYawShift.IsActive) HandOffHeadToTracking();
                return;
            }

            // Head stability: target motion inside the dead-band is absorbed by the eyes while
            // the head holds its aim. Heads reposition deliberately — they never micro-track the
            // way eyes do. This belongs to the tracking lane alone: applied during a movement it
            // would eat the movement's own launch.
            float band = Mathf.Max(0f, profile.HeadStabilityDegrees);
            _heldGoalYaw = HoldInBand(_heldGoalYaw, tracked.x, band);
            _heldGoalPitch = HoldInBand(_heldGoalPitch, tracked.y, band);

            _headYaw = _headYawMotor.Step(_heldGoalYaw, speed, SafetyEnvelope.HeadMaxAccel, dt);
            _headPitch = _headPitchMotor.Step(_heldGoalPitch, speed, SafetyEnvelope.HeadMaxAccel, dt);
        }

        /// <summary>
        ///     Executes the chest's share. Same two lanes as the head, with its own duration law:
        ///     a chest is heavier than a head and a chest that keeps up with one reads wrong.
        /// </summary>
        private void ExecuteTorsoLane(
            Vector2 tracked, bool startMovement, float tempo, ConvaiGazeProfile profile, float dt)
        {
            float speed = Mathf.Max(1f, profile.MaxTorsoAngularSpeed);
            float skew = Mathf.Clamp01(profile.MovementSkew);

            if (startMovement)
            {
                float distance = Vector2.Distance(new Vector2(_torsoYaw, _torsoPitch), tracked);
                float duration = MovementSeconds(
                    distance, profile.TorsoTurnBaseSeconds, profile.TorsoTurnSecondsPerDegree, tempo);
                if (duration > 0f)
                {
                    float yawVelocity = LaneVelocity(in _torsoYawShift, in _torsoYawMotor);
                    float pitchVelocity = LaneVelocity(in _torsoPitchShift, in _torsoPitchMotor);
                    _torsoYawShift.Begin(_torsoYaw, yawVelocity, duration);
                    _torsoPitchShift.Begin(_torsoPitch, pitchVelocity, duration);
                }
            }

            if (_torsoYawShift.IsActive)
            {
                _torsoYaw = _torsoYawShift.Step(tracked.x, speed, SafetyEnvelope.TorsoMaxAccel, skew, dt);
                _torsoPitch = _torsoPitchShift.Step(
                    tracked.y, speed, SafetyEnvelope.TorsoMaxAccel, skew, dt);

                if (!_torsoYawShift.IsActive)
                {
                    _torsoYawMotor.Seed(_torsoYaw);
                    _torsoPitchMotor.Seed(_torsoPitch);
                }

                return;
            }

            _torsoYaw = _torsoYawMotor.Step(tracked.x, speed, SafetyEnvelope.TorsoMaxAccel, dt);
            _torsoPitch = _torsoPitchMotor.Step(tracked.y, speed, SafetyEnvelope.TorsoMaxAccel, dt);
        }

        /// <summary>
        ///     Seeds the head's tracking filters and stability band where the movement just
        ///     finished, so the lane change is invisible.
        /// </summary>
        private void HandOffHeadToTracking()
        {
            _headYawMotor.Seed(_headYaw);
            _headPitchMotor.Seed(_headPitch);
            _heldGoalYaw = _headYaw;
            _heldGoalPitch = _headPitch;
        }

        /// <summary>
        ///     Advances the neck/head split displacement for this frame. See <see cref="ChainLag" />.
        /// </summary>
        /// <remarks>
        ///     Semi-implicit Euler, which is unconditionally stable for this system at any frame
        ///     rate the engine actually runs at, and — unlike an analytic solution — stays correct
        ///     when the drive changes every frame, which it does.
        /// </remarks>
        private void UpdateChainLag(Vector2 aim, ConvaiGazeProfile profile, float dt)
        {
            float followThrough = Mathf.Clamp01(profile.ChainFollowThrough);

            // A zero amount must be a complete no-op, not a system that happens to settle at
            // zero: a character authored to turn rigidly should be bit-identical to one built
            // before this existed.
            if (followThrough <= 0f)
            {
                _chainLag = 0f;
                _chainLagVelocity = 0f;
                _previousAim = aim;
                _hasPreviousAim = true;
                return;
            }

            // Semi-implicit Euler on this system is stable to about 13 fps and divergent below it.
            // The clamp costs nothing at any playable frame rate and turns a pathological hitch
            // into a slightly slow response instead of a split that flaps against its bounds.
            float step = Mathf.Min(dt, 1f / 20f);

            float speed = _hasPreviousAim ? Vector2.Distance(aim, _previousAim) / dt : 0f;
            _previousAim = aim;
            _hasPreviousAim = true;

            float drive = followThrough * ChainLag.MaxShare *
                          Mathf.Clamp01(speed / ChainLag.ReferenceSpeed);

            float acceleration =
                ChainLag.Stiffness * (drive - _chainLag) - ChainLag.Damping * _chainLagVelocity;
            _chainLagVelocity += acceleration * step;
            _chainLag = Mathf.Clamp(
                _chainLag + _chainLagVelocity * step, ChainLag.MinClamp, ChainLag.MaxClamp);
        }

        /// <summary>Whichever lane currently owns the channel is the one whose velocity is real.</summary>
        private static float LaneVelocity(in BallisticMotor shift, in MotorFilter tracking) =>
            shift.IsActive ? shift.Velocity : tracking.Velocity;

        /// <summary>
        ///     How long a movement of this size should take — the main sequence.
        /// </summary>
        /// <remarks>
        ///     Duration grows linearly with amplitude from a non-zero floor, which is the whole
        ///     point: the floor is what stops small movements from being the sharpest thing on
        ///     screen. A time-optimal trajectory under an acceleration cap scales as the square
        ///     root of distance, so a 5° correction finishes in a tenth of the time of a 40° turn
        ///     rather than half of it, and reads as a twitch.
        /// </remarks>
        private static float MovementSeconds(
            float amplitudeDegrees, float baseSeconds, float secondsPerDegree, float tempoScale) =>
            (Mathf.Max(0f, baseSeconds) + Mathf.Max(0f, secondsPerDegree) * Mathf.Abs(amplitudeDegrees)) *
            Mathf.Max(0.1f, tempoScale);

        /// <summary>
        ///     Duration multiplier for what kind of movement this is. Anything the character
        ///     chose to do is <see cref="GazeMovementUrgency.Neutral" /> — including looking at
        ///     the player, which is an ordinary act of attention and not an alarm.
        /// </summary>
        private static float TempoScale(GazeMovementUrgency urgency, ConvaiGazeProfile profile) =>
            urgency switch
            {
                GazeMovementUrgency.Relaxed => Mathf.Max(0.1f, profile.IdleDriftTempoScale),
                GazeMovementUrgency.Urgent => UrgentTempoScale,
                _ => 1f
            };

        /// <summary>
        ///     Keeps <paramref name="held" /> within <paramref name="band" /> degrees of
        ///     <paramref name="raw" />: unchanged while inside the band, dragged along at
        ///     the boundary while the raw goal keeps moving.
        /// </summary>
        private static float HoldInBand(float held, float raw, float band)
        {
            float excess = Mathf.Abs(raw - held) - band;
            return excess <= 0f ? held : Mathf.MoveTowards(held, raw, excess);
        }

        private void Apply(GazeChainCalibration chain, ConvaiGazeProfile profile)
        {
            const float epsilon = 0.005f;
            bool headActive = Mathf.Abs(_appliedHeadYaw) > epsilon || Mathf.Abs(_appliedHeadPitch) > epsilon;
            bool torsoActive = Mathf.Abs(_torsoYaw) > epsilon || Mathf.Abs(_torsoPitch) > epsilon;
            bool rollActive = Mathf.Abs(_appliedHeadRoll) > epsilon;
            if (!headActive && !torsoActive && !rollActive) return;

            Transform reference = chain.Root;
            bool calibrated = chain.TryGetGazeReferenceFrame(out GazeReferenceFrame referenceFrame);

            // ---- Compose every delta first, write once. ----
            Quaternion chestDelta = Quaternion.identity;
            Quaternion upperChestDelta = Quaternion.identity;

            if (torsoActive)
            {
                Quaternion torsoSwing = BuildAimSwing(reference, calibrated, referenceFrame, _torsoYaw, _torsoPitch);
                if (chain.Chest != null && chain.UpperChest != null)
                {
                    GazeSolverMath.SplitAimSwing(torsoSwing, ProceduralPoseCompositor.ChestAimShare,
                        out chestDelta, out upperChestDelta);
                }
                else if (chain.UpperChest != null)
                {
                    upperChestDelta = torsoSwing;
                }
                else if (chain.Chest != null)
                {
                    chestDelta = torsoSwing;
                }
            }

            Quaternion neckDelta = Quaternion.identity;
            Quaternion headDelta = Quaternion.identity;

            if (headActive || rollActive)
            {
                Quaternion headSwing = BuildAimSwing(reference, calibrated, referenceFrame, _appliedHeadYaw, _appliedHeadPitch);

                // Gesture roll: Head only, never distributed to the Neck the way the aim is. A
                // tilt reads as a head gesture specifically when it stays local to the head —
                // the NeckShare split exists to make aim turns look anatomically continuous,
                // which does not apply to a tilt with no torso/neck counterpart to share with.
                // The roll axis is the AIMED forward, not the frame's neutral forward: once the
                // head is yawed, the neutral forward is no longer the head's long axis and a
                // roll about it reads as a tilt-plus-yaw rather than a tilt.
                Vector3 neutralForward = calibrated ? referenceFrame.Forward : reference.forward;
                Quaternion roll = GazeSolverMath.RollSwing(headSwing * neutralForward, _appliedHeadRoll);

                if (chain.Neck != null && chain.Head != null)
                {
                    // The split, displaced by however much overlapping action is currently in
                    // the chain. The swing being divided is untouched — only where the division
                    // falls moves, which is what keeps the aim exact while the chain flexes.
                    float neckShare = Mathf.Clamp01(profile.NeckShare + _chainLag);
                    GazeSolverMath.SplitAimSwing(headSwing, neckShare, out neckDelta, out headDelta);
                    // Roll is composed into the SAME write as the head's aim rather than a
                    // second write+record, so each bone is written exactly once per frame.
                    headDelta = roll * headDelta;
                }
                else if (chain.Head != null)
                {
                    headDelta = roll * headSwing;
                }
                else if (chain.Neck != null)
                {
                    neckDelta = roll * headSwing;
                }
            }

            // Route each bone to whichever guard actually owns it.
            //
            // The shared compositor is the single writer for every bone it has bound — Body
            // Language owns it, and it is built for several writers composing onto the same
            // bone in one frame (it keeps the FIRST pre-write value, so a restore unwinds to
            // the animated pose, never to an intermediate composite). Running gaze's own guard
            // over those same bones would be two restore protocols on one bone: a double-unwind
            // waiting to happen.
            //
            // But a compositor can be bound to a SUBSET of this chain — it binds from its own
            // rig resolution, and a character can have a spine bound and no head chain. Handing
            // it a delta for a bone it does not hold would silently drop that write, so bones it
            // does not own fall to the gaze-private guard. The two guards then cover disjoint
            // sets, which is the only arrangement that is safe.
            bool sinkBound = _poseSink != null && _poseSink.IsBound;
            bool sinkHasChest = sinkBound && _poseSink.Chest == chain.Chest;
            bool sinkHasUpperChest = sinkBound && _poseSink.UpperChest == chain.UpperChest;
            bool sinkHasNeck = sinkBound && _poseSink.Neck == chain.Neck;
            bool sinkHasHead = sinkBound && _poseSink.Head == chain.Head;

            if (sinkBound)
            {
                _poseSink.ComposeGazeAim(
                    sinkHasChest ? chestDelta : Quaternion.identity,
                    sinkHasUpperChest ? upperChestDelta : Quaternion.identity,
                    sinkHasNeck ? neckDelta : Quaternion.identity,
                    sinkHasHead ? headDelta : Quaternion.identity);
            }

            if (!sinkHasChest) ApplyGuarded(chain.Chest, chestDelta);
            if (!sinkHasUpperChest) ApplyGuarded(chain.UpperChest, upperChestDelta);
            if (!sinkHasNeck) ApplyGuarded(chain.Neck, neckDelta);
            if (!sinkHasHead) ApplyGuarded(chain.Head, headDelta);
        }

        /// <summary>Aim swing in whichever reference the rig calibrated to.</summary>
        private static Quaternion BuildAimSwing(
            Transform reference, bool calibrated, in GazeReferenceFrame referenceFrame, float yaw, float pitch) =>
            calibrated
                ? GazeSolverMath.AimSwing(referenceFrame, yaw, pitch)
                : GazeSolverMath.AimSwing(reference, yaw, pitch);

        /// <summary>
        ///     Writes one composed world-space delta through the pose-write guard so the write
        ///     can be unwound next frame if no animation source re-poses the bone. Exactly one
        ///     write and one record per bone, which is what keeps the guard's fixed 4-slot
        ///     capacity — chest, upper chest, neck, head — sufficient.
        /// </summary>
        private void ApplyGuarded(Transform bone, Quaternion worldDelta)
        {
            if (bone == null || worldDelta == Quaternion.identity) return;

            Quaternion preWrite = bone.localRotation;
            GazeSolverMath.ApplyDelta(bone, worldDelta);
            _writeGuard.Record(bone, preWrite);
        }
    }
}
