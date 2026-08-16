using Convai.Modules.Gaze.Data;
using UnityEngine;

namespace Convai.Modules.Gaze.Core.Solvers
{
    /// <summary>Per-frame input for <see cref="EyeSolver.Solve" />.</summary>
    internal struct EyeSolveInput
    {
        public GazeChainCalibration Chain;
        public ConvaiGazeProfile Profile;
        public float DeltaTime;

        public Vector3 TargetPoint;
        public bool HasTarget;
        public float Engagement;

        public Vector2 AmbientAngles;
        public bool AmbientActive;

        /// <summary>Fixation micro-motion + face-scan offset (degrees), added outside saccades.</summary>
        public Vector2 MicroOffset;

        /// <summary>Aversion beat offset (degrees) — part of the desired aim, so it saccades.</summary>
        public Vector2 AversionOffset;

        public int GenerationId;
        public bool Teleported;
        public float FixationLiveliness;

        /// <summary>
        ///     Emotion-driven saccade tempo multiplier (1 = unmodified): scales how fast
        ///     the saccadic-latency wait timer fills, so &gt;1 quickens reaction and &lt;1 slows it.
        /// </summary>
        public float SaccadeTempoScale;

        /// <summary>When false, angles are solved but eye bones are not written (blendshape backend).</summary>
        public bool ApplyToBones;

        /// <summary>
        ///     True when the eyes are driven by <c>EyeLook*</c> blendshapes (no eye bones).
        ///     Vergence still runs in this mode using synthetic eye positions, so close
        ///     targets converge on blendshape-only rigs.
        /// </summary>
        public bool LookShapesActive;
    }

    /// <summary>
    ///     The oculomotor stage: a fixation/saccade/pursuit state machine driving conjugate
    ///     eye rotation with per-eye vergence. Saccades are ballistic and follow the
    ///     main-sequence (duration grows with amplitude); slow targets are tracked with
    ///     smooth pursuit and catch-up saccades fire when pursuit falls behind; and the
    ///     oculomotor range clamps rotation with soft compression.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Angles are expressed relative to the head-following rest forward, so the eyes
    ///         re-center in the orbit as the head catches up. That is a consequence of the frame,
    ///         not a reflex, and it only holds while the goal is being recomputed every frame —
    ///         which is why <see cref="StabilizeAgainstHeadMotion" /> exists. See its remarks.
    ///     </para>
    /// </remarks>
    internal sealed class EyeSolver
    {
        /// <summary>
        ///     Vergence responds slower than conjugate eye motion (it is a fusional reflex,
        ///     not a saccadic one): convergence changes glide underneath ballistic flight.
        /// </summary>
        private const float VergenceSharpness = 10f;

        /// <summary>
        ///     Saccades land slightly short of the target (hypometria, ~8–10% for large
        ///     amplitudes); pursuit closes the residual right after, which is what gives a
        ///     real gaze shift its soft landing instead of a dead mechanical stop.
        /// </summary>
        private const float SaccadeUndershootFraction = 0.92f;

        /// <summary>Exponential smoothing rate (per second) for the tracked target velocity.</summary>
        private const float TargetVelocitySmoothing = 8f;

        /// <summary>
        ///     How much saccadic reaction latency varies between one look and the next, as a
        ///     fraction of the profile's figure. At the default the shipped 0.12 s spans roughly
        ///     78–162 ms, which is about the spread a real person shows.
        /// </summary>
        /// <remarks>
        ///     A fixed latency is a subtle but persistent tell: every look begins after exactly
        ///     the same pause, and the regularity is legible even when the number is right.
        ///     Derived from the target's generation rather than drawn from the shared
        ///     deterministic stream — reproducible for a given sequence of looks, and, more
        ///     importantly, it does not consume draws that every other seeded system's sequence
        ///     depends on. A person's reaction time is also fairly consistent within one episode
        ///     of attention, so holding it constant across catch-up saccades on the same target
        ///     is the accurate behaviour rather than a shortcut.
        /// </remarks>
        private const float ReactionJitterFraction = 0.35f;

        /// <summary>
        ///     Ceiling on the tracked target speed (m/s). Real pursuit only leads believable
        ///     conversational motion; clamping keeps a glitchy one-frame jump in the target
        ///     transform from throwing the lead far off-target.
        /// </summary>
        private const float MaxTrackedTargetSpeed = 3f;

        private enum EyePhase
        {
            Idle,
            Fixating,
            Saccade,
            Pursuit
        }

        private EyePhase _phase = EyePhase.Idle;
        private float _yaw;             // cyclopean, degrees relative to rest forward
        private float _pitch;
        private float _saccadeStartYaw;
        private float _saccadeStartPitch;
        private float _saccadeEndYaw;
        private float _saccadeEndPitch;
        private float _saccadeTime;
        private float _saccadeDuration;
        private float _saccadeWaitTimer;
        private float _leftVergence;    // per-eye yaw offsets (inward negative/positive)
        private float _rightVergence;
        private int _lastGeneration = int.MinValue;

        private Vector3 _targetVelocity;    // smoothed world velocity of the target point (m/s)
        private Vector3 _lastTargetPoint;
        private bool _hasLastTargetPoint;

        private Vector3 _lastRestForwardWorld;  // where the head aimed last frame, in world space
        private bool _hasLastRestForward;

        /// <summary>Final left-eye orbit angles (yaw/pitch degrees) this frame.</summary>
        public Vector2 LeftEyeAngles { get; private set; }

        /// <summary>Final right-eye orbit angles (yaw/pitch degrees) this frame.</summary>
        public Vector2 RightEyeAngles { get; private set; }

        /// <summary>Current phase label for HUD/diagnostics.</summary>
        public string PhaseName => _phase.ToString();

        /// <summary>Amplitude (degrees) of the saccade started this tick; 0 when none.</summary>
        public float SaccadeStartedAmplitude { get; private set; }

        /// <summary>
        ///     Live angular distance (degrees, (yaw,pitch) space) between the raw cyclopean aim
        ///     goal toward the target — computed before oculomotor-range clamping — and the
        ///     achieved cyclopean eye angles after clamping and state-machine dynamics. Transient
        ///     during saccade flight by design. <see cref="float.NaN" /> when there is no target
        ///     or the eye stage has no bones and no look shapes (internal seam for diagnostics).
        /// </summary>
        internal float ContactErrorDegrees { get; private set; } = float.NaN;

        public void Reset()
        {
            _phase = EyePhase.Idle;
            _yaw = 0f;
            _pitch = 0f;
            _saccadeTime = 0f;
            _saccadeDuration = 0f;
            _saccadeWaitTimer = 0f;
            _leftVergence = 0f;
            _rightVergence = 0f;
            _lastGeneration = int.MinValue;
            _targetVelocity = Vector3.zero;
            _lastTargetPoint = Vector3.zero;
            _hasLastTargetPoint = false;
            _lastRestForwardWorld = Vector3.zero;
            _hasLastRestForward = false;
            LeftEyeAngles = Vector2.zero;
            RightEyeAngles = Vector2.zero;
            SaccadeStartedAmplitude = 0f;
            ContactErrorDegrees = float.NaN;
        }

        public void Solve(in EyeSolveInput input)
        {
            SaccadeStartedAmplitude = 0f;
            ContactErrorDegrees = float.NaN;

            GazeChainCalibration chain = input.Chain;
            ConvaiGazeProfile profile = input.Profile;
            if (chain == null || profile == null || chain.Root == null) return;

            float dt = input.DeltaTime > 0f ? input.DeltaTime : 1f / 60f;

            // Reset eye bones to their rest pose over the freshly animated head so all
            // angle math is relative to "looking straight ahead with the current head".
            if (input.ApplyToBones && chain.HasEyeBones)
            {
                chain.LeftEye.localRotation = chain.LeftEyeRestLocal;
                chain.RightEye.localRotation = chain.RightEyeRestLocal;
            }

            bool calibrated = chain.TryGetGazeReferenceFrame(out GazeReferenceFrame referenceFrame);
            // The cyclopean rest is the head-carried "straight ahead" (the calibrated root
            // forward on calibrated rigs — see GazeChainCalibration.CaptureEyeRest). It must
            // NOT be the average of the per-eye rest forwards: on rigs whose eye bones are
            // authored with unusual local axes (e.g. one eye's forward 90° off the root
            // forward) that average points off-axis, so a dead-ahead target would read as a
            // huge fake excursion and saturate the oculomotor clamp. Per-eye rest axes are
            // handled below as reframing offsets instead.
            Vector3 restForward = chain.CurrentEyeRestForward;
            if (restForward.sqrMagnitude < 1e-6f) restForward = chain.Root.forward;
            restForward.Normalize();

            bool hasRestAngles = calibrated
                ? GazeSolverMath.TryGetDirectionYawPitch(referenceFrame, restForward, out float restYaw, out float restPitch)
                : GazeSolverMath.TryGetDirectionYawPitch(chain.Root, restForward, out restYaw, out restPitch);
            if (!hasRestAngles)
                return;

            float leftRestYaw = restYaw;
            float leftRestPitch = restPitch;
            float rightRestYaw = restYaw;
            float rightRestPitch = restPitch;
            if (calibrated)
            {
                GazeSolverMath.TryGetDirectionYawPitch(referenceFrame, chain.CurrentLeftEyeRestForward,
                    out leftRestYaw, out leftRestPitch);
                GazeSolverMath.TryGetDirectionYawPitch(referenceFrame, chain.CurrentRightEyeRestForward,
                    out rightRestYaw, out rightRestPitch);
            }

            ComputeDesiredAngles(in input, chain, calibrated, referenceFrame, restYaw, restPitch,
                out float desiredYaw, out float desiredPitch);

            // Raw cyclopean aim goal toward the target, before aversion/micro dressing and
            // before oculomotor-range clamping — the reference the contact-fidelity error is
            // measured against.
            float rawGoalYaw = desiredYaw;
            float rawGoalPitch = desiredPitch;

            // Aversion beats are part of the desired aim, so the state machine saccades
            // onto the look-away point exactly like a real glance.
            desiredYaw += input.AversionOffset.x;
            desiredPitch += input.AversionOffset.y;

            // Micro-motion rides on top of fixations and pursuit, never on ballistic jumps.
            if (_phase != EyePhase.Saccade)
            {
                desiredYaw += input.MicroOffset.x * input.FixationLiveliness;
                desiredPitch += input.MicroOffset.y * input.FixationLiveliness;
            }

            desiredYaw = GazeSolverMath.SoftClamp(desiredYaw, profile.EyeMaxYawDegrees, profile.EyeSoftLimitFraction);
            desiredPitch = ClampPitch(desiredPitch, profile);

            // Predictive pursuit: the angular offset from leading the target along its velocity,
            // applied only inside the pursuit phase so ballistic saccades still fly to the true
            // point. Zero when disabled, on a static target, or right after a re-acquisition.
            UpdatePursuitLead(in input, chain, calibrated, referenceFrame, dt, out float leadDeltaYaw, out float leadDeltaPitch);

            // Cancel the head's own rotation out of the eye state before the state machine runs,
            // so its branches can reason as though the head stood still. Needs the goal, hence
            // its position here rather than earlier — see the method's remarks.
            StabilizeAgainstHeadMotion(in input, chain, calibrated, referenceFrame,
                restForward, restYaw, restPitch, desiredYaw, desiredPitch);

            AdvanceStateMachine(in input, profile, desiredYaw, desiredPitch, leadDeltaYaw, leadDeltaPitch, dt);

            _yaw = GazeSolverMath.SoftClamp(_yaw, profile.EyeMaxYawDegrees, profile.EyeSoftLimitFraction);
            _pitch = ClampPitch(_pitch, profile);

            bool eyeStageActive = chain.HasEyeBones || input.LookShapesActive;
            ContactErrorDegrees = input.HasTarget && eyeStageActive
                ? Vector2.Distance(new Vector2(rawGoalYaw, rawGoalPitch), new Vector2(_yaw, _pitch))
                : float.NaN;

            SolveVergence(in input, chain, calibrated, referenceFrame, profile, dt);

            // Weighted by engagement rather than switched on a threshold. These offsets can be
            // several degrees on a rig whose eye bones carry an authored rest axis, and stepping
            // them to zero the frame engagement crosses a threshold put a visible eye pop at
            // exactly the moment a look is released — the transition the whole movement rework
            // exists to smooth. Engagement already decays continuously as commitment ramps down,
            // so weighting by it costs nothing and removes the step.
            float reframe = calibrated && input.HasTarget ? Mathf.Clamp01(input.Engagement) : 0f;
            float leftBaseYaw = (restYaw - leftRestYaw) * reframe;
            float rightBaseYaw = (restYaw - rightRestYaw) * reframe;
            float leftBasePitch = (restPitch - leftRestPitch) * reframe;
            float rightBasePitch = (restPitch - rightRestPitch) * reframe;
            // The base offsets re-express the shared aim into each eye's own calibrated rest
            // frame — they are axis-convention reframing, not physiological orbit rotation, so
            // they stay OUTSIDE the oculomotor clamp. The clamp bounds the pupil's deviation
            // from straight-ahead (_yaw + vergence); clamping the reframing away would pin any
            // eye whose authored rest axis sits far from the root forward permanently off-target.
            float leftYaw = GazeSolverMath.SoftClamp(_yaw + _leftVergence, profile.EyeMaxYawDegrees, profile.EyeSoftLimitFraction) + leftBaseYaw;
            float rightYaw = GazeSolverMath.SoftClamp(_yaw + _rightVergence, profile.EyeMaxYawDegrees, profile.EyeSoftLimitFraction) + rightBaseYaw;
            LeftEyeAngles = new Vector2(leftYaw, _pitch + leftBasePitch);
            RightEyeAngles = new Vector2(rightYaw, _pitch + rightBasePitch);

            if (input.ApplyToBones && chain.HasEyeBones)
            {
                if (calibrated)
                {
                    ApplyEye(chain.LeftEye, referenceFrame, chain.CurrentLeftEyeRestForward, leftRestYaw, leftRestPitch, LeftEyeAngles);
                    ApplyEye(chain.RightEye, referenceFrame, chain.CurrentRightEyeRestForward, rightRestYaw, rightRestPitch, RightEyeAngles);
                }
                else
                {
                    ApplyEye(chain.LeftEye, chain.Root, restForward, restYaw, restPitch, LeftEyeAngles);
                    ApplyEye(chain.RightEye, chain.Root, restForward, restYaw, restPitch, RightEyeAngles);
                }
            }
        }

        /// <summary>
        ///     The vestibulo-ocular reflex: counter-rotates the eye state by however much the
        ///     head turned since last frame, so the eyes hold what they are looking at instead of
        ///     being carried around by the head.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>Why this is needed at all.</b> <see cref="_yaw" /> is an eye-IN-HEAD angle.
        ///         Two branches of the state machine deliberately hold it still: the saccadic
        ///         latency wait, and the ballistic flight between a saccade's captured start and
        ///         end. Holding an eye-in-head angle still is only "holding a fixation" while the
        ///         head is also still. The moment the head is turning — which, during a gaze
        ///         shift, is exactly when those two branches run — a held eye-in-head angle sweeps
        ///         the eyes across the world at head speed.
        ///     </para>
        ///     <para>
        ///         The measured symptom was a staircase: the first saccade reached the target and
        ///         brought the error under 1.5°, the still-turning head dragged the eyes back off
        ///         it to 24°, a catch-up saccade fired, and so on — three saccades for one
        ///         stationary target, with the gaze visibly elsewhere in between. The same shot
        ///         with this stage in place takes one saccade and strays 3°.
        ///     </para>
        ///     <para>
        ///         Correcting the state rather than the two branches is deliberate. Gaze-in-space
        ///         is the quantity the oculomotor system actually controls; eye-in-head is a
        ///         residual of it. Doing the subtraction once, before any branch runs, is what
        ///         makes that true here too — and it removes the steady-state tracking lag from
        ///         the pursuit and fixation branches for free, because they are then holding a
        ///         stationary goal instead of chasing a moving one.
        ///     </para>
        ///     <para>
        ///         Unity gain, and no profile knob. Biological VOR is partially suppressed during
        ///         large gaze shifts so the eyes can contribute to the shift; here they must not,
        ///         because <c>GazeActuatorLadder</c> has already decided the head's share and the
        ///         eyes are its remainder. Suppressing the reflex would re-introduce precisely the
        ///         head/eye coupling the ladder exists to prevent.
        ///     </para>
        ///     <para>
        ///         <b>The reflex is bounded by the goal, and this is load-bearing.</b> "Hold the
        ///         world point" is only the right instruction while the world point the eyes are on
        ///         is the one they are trying to reach. It is not, in the two cases that matter: a
        ///         saccade lands slightly short by design (hypometria), and it lands a long way
        ///         short when the target was outside the oculomotor range at launch and the aim was
        ///         clamped. Stabilizing those faithfully makes the eyes hold a point that is not
        ///         the target, and — once the head turns past it — sweep backwards through the
        ///         orbit, away from what the character is looking at. Measured, that is the worst
        ///         reading of all: the head arrives at the player while the eyes drift back toward
        ///         whatever was being pointed at.
        ///     </para>
        ///     <para>
        ///         So the compensation is clamped to the interval between where the eyes are and
        ///         where they are aiming: it may carry them toward the goal, never past it and
        ///         never away from it. An eye pinned at its range limit therefore stays pinned
        ///         while the head turns — which is what an eye at the end of its travel does — and
        ///         starts re-centering exactly when the head brings the target back within reach.
        ///     </para>
        /// </remarks>
        private void StabilizeAgainstHeadMotion(
            in EyeSolveInput input,
            GazeChainCalibration chain,
            bool calibrated,
            in GazeReferenceFrame referenceFrame,
            Vector3 restForward,
            float restYaw,
            float restPitch,
            float goalYaw,
            float goalPitch)
        {
            Vector3 previousForward = _lastRestForwardWorld;
            bool hadPrevious = _hasLastRestForward;
            _lastRestForwardWorld = restForward;
            _hasLastRestForward = true;

            // No world point to hold: ambient exploration aims in head space by construction, so
            // those angles must ride WITH the head rather than resist it. Also skipped on the
            // first frame and across a cut, where the previous forward means nothing.
            if (!hadPrevious || !input.HasTarget || input.Engagement <= 0.0001f || input.Teleported)
                return;

            // Last frame's head aim, measured in THIS frame's reference frame. Measuring both
            // ends in the same frame is what makes a reference frame that rotated with the body
            // cancel out, leaving only the head's own contribution.
            bool measured = calibrated
                ? GazeSolverMath.TryGetDirectionYawPitch(
                    referenceFrame, previousForward, out float lastYaw, out float lastPitch)
                : GazeSolverMath.TryGetDirectionYawPitch(
                    chain.Root, previousForward, out lastYaw, out lastPitch);
            if (!measured) return;

            float headYawDelta = Mathf.DeltaAngle(lastYaw, restYaw);
            float headPitchDelta = restPitch - lastPitch;
            if (Mathf.Abs(headYawDelta) < 1e-4f && Mathf.Abs(headPitchDelta) < 1e-4f) return;

            // What the reflex is actually allowed to move, after the goal bound above.
            float appliedYaw = TowardGoal(_yaw, _yaw - headYawDelta, goalYaw) - _yaw;
            float appliedPitch = TowardGoal(_pitch, _pitch - headPitchDelta, goalPitch) - _pitch;
            if (appliedYaw == 0f && appliedPitch == 0f) return;

            _yaw += appliedYaw;
            _pitch += appliedPitch;

            // The in-flight trajectory is carried by the same amount, so a saccade still lands
            // where it was aimed however far the head travelled underneath it. Only while one is
            // actually flying — outside that, both ends are re-seeded by StartSaccade anyway.
            if (_phase != EyePhase.Saccade) return;

            _saccadeStartYaw += appliedYaw;
            _saccadeStartPitch += appliedPitch;
            _saccadeEndYaw += appliedYaw;
            _saccadeEndPitch += appliedPitch;
        }

        /// <summary>
        ///     <paramref name="compensated" /> restricted to the interval between
        ///     <paramref name="current" /> and <paramref name="goal" /> — the reflex may carry the
        ///     eye toward where it is aiming, never past it and never away from it.
        /// </summary>
        private static float TowardGoal(float current, float compensated, float goal) =>
            Mathf.Clamp(compensated, Mathf.Min(current, goal), Mathf.Max(current, goal));

        private void ComputeDesiredAngles(
            in EyeSolveInput input,
            GazeChainCalibration chain,
            bool calibrated,
            in GazeReferenceFrame referenceFrame,
            float restYaw,
            float restPitch,
            out float desiredYaw,
            out float desiredPitch)
        {
            desiredYaw = 0f;
            desiredPitch = 0f;

            if (input.HasTarget && input.Engagement > 0.0001f)
            {
                // The eyes commit fully the moment a target is engaged: real eyes never
                // sweep toward a target as commitment ramps — they jump. Scaling the aim
                // by engagement would drag the desired point through space and shatter
                // one acquisition into a staircase of catch-up saccades; the gradual
                // "committing" read comes from the head/torso ramps instead.
                Vector3 eyeCenter = chain.EyeCenterPosition;
                bool hasTargetAngles = calibrated
                    ? GazeSolverMath.TryGetYawPitch(referenceFrame, eyeCenter, input.TargetPoint, out float yawAbs, out float pitchAbs)
                    : GazeSolverMath.TryGetYawPitch(chain.Root, eyeCenter, input.TargetPoint, out yawAbs, out pitchAbs);
                if (!hasTargetAngles)
                    return;

                desiredYaw = Mathf.DeltaAngle(restYaw, yawAbs);
                desiredPitch = pitchAbs - restPitch;
                return;
            }

            if (input.AmbientActive)
            {
                desiredYaw = input.AmbientAngles.x;
                desiredPitch = input.AmbientAngles.y;
            }
        }

        private void AdvanceStateMachine(
            in EyeSolveInput input,
            ConvaiGazeProfile profile,
            float desiredYaw,
            float desiredPitch,
            float leadDeltaYaw,
            float leadDeltaPitch,
            float dt)
        {
            float errorYaw = desiredYaw - _yaw;
            float errorPitch = desiredPitch - _pitch;
            float error = Mathf.Sqrt(errorYaw * errorYaw + errorPitch * errorPitch);

            bool freshTarget = input.GenerationId != _lastGeneration || input.Teleported;
            _lastGeneration = input.GenerationId;

            if (_phase == EyePhase.Saccade)
            {
                _saccadeTime += dt;
                float t = _saccadeDuration > 1e-5f ? Mathf.Clamp01(_saccadeTime / _saccadeDuration) : 1f;
                // Minimum-jerk profile: ballistic acceleration with a soft landing.
                float ease = t * t * t * (10f + t * (-15f + 6f * t));
                _yaw = Mathf.Lerp(_saccadeStartYaw, _saccadeEndYaw, ease);
                _pitch = Mathf.Lerp(_saccadeStartPitch, _saccadeEndPitch, ease);

                if (t >= 1f)
                    _phase = EyePhase.Pursuit;
                return;
            }

            bool wantsSaccade = error > profile.SaccadeDeadzoneDegrees &&
                                (freshTarget || error > profile.CatchUpErrorDegrees);
            if (wantsSaccade)
            {
                // Saccadic latency: real eyes take ~0.1–0.25 s to respond to a new or
                // displaced target. The gaze holds its current fixation during the wait —
                // gliding toward the upcoming point would pre-consume the jump and turn
                // the ballistic shift into a sweep. "Holds its fixation" means holds the
                // WORLD point, which is what StabilizeAgainstHeadMotion makes true of the
                // untouched angle below; without it this branch holds an eye-in-head angle
                // and the turning head sweeps the eyes off the thing they were looking at.
                //
                // Emotional gaze signature: an unset tempo (0 on inputs built before this
                // field existed) reads as unmodified (1), the same fallback pattern used for
                // LidApertureScale elsewhere in the emotion table — a faster tempo fills the
                // wait timer sooner (quicker reaction), a slower one takes longer.
                float tempoScale = input.SaccadeTempoScale > 0f ? input.SaccadeTempoScale : 1f;
                if (freshTarget) _saccadeWaitTimer = 0f;
                _saccadeWaitTimer += dt * tempoScale;
                if (_saccadeWaitTimer >= profile.SaccadeReactionSeconds * ReactionJitter(input.GenerationId))
                {
                    _saccadeWaitTimer = 0f;
                    StartSaccade(profile, desiredYaw, desiredPitch, error);
                    return;
                }

                _phase = EyePhase.Fixating;
                return;
            }

            _saccadeWaitTimer = 0f;

            if (error > profile.SaccadeDeadzoneDegrees * 0.25f)
            {
                // Smooth pursuit — the one phase that leads a moving target.
                float pursuitYaw = desiredYaw + leadDeltaYaw;
                float pursuitPitch = desiredPitch + leadDeltaPitch;

                float alpha = 1f - Mathf.Exp(-RecenteringSharpness(profile, pursuitYaw) * dt);
                _yaw += (pursuitYaw - _yaw) * alpha;
                _pitch += (pursuitPitch - _pitch) * alpha;
                _phase = EyePhase.Pursuit;
                return;
            }

            _phase = input.HasTarget || input.AmbientActive ? EyePhase.Fixating : EyePhase.Idle;
            float fixationAlpha = 1f - Mathf.Exp(-RecenteringSharpness(profile, desiredYaw) * dt);
            _yaw = Mathf.Lerp(_yaw, desiredYaw, fixationAlpha);
            _pitch = Mathf.Lerp(_pitch, desiredPitch, fixationAlpha);
        }

        /// <summary>
        ///     Per-look multiplier on the saccadic reaction latency, in
        ///     <c>1 ± ReactionJitterFraction</c>. See that constant for why it is hashed from the
        ///     generation rather than drawn from the shared random stream.
        /// </summary>
        private static float ReactionJitter(int generationId)
        {
            // A small integer avalanche (Wang-style mix), taken to the low 24 bits so the float
            // conversion is exact. Any decent bit-mixer works here; what matters is that
            // consecutive generation ids — which is exactly what a sequence of looks produces —
            // land nowhere near each other.
            uint hash = (uint)generationId * 2654435761u;
            hash ^= hash >> 15;
            hash *= 2246822519u;
            hash ^= hash >> 13;

            float unit = (hash & 0xFFFFFFu) / (float)0xFFFFFF;
            return 1f + ReactionJitterFraction * (unit * 2f - 1f);
        }

        /// <summary>
        ///     Tracks the target's world velocity (heavily smoothed, speed-clamped) and returns
        ///     the yaw/pitch offset from aiming <c>pursuitLeadSeconds</c> ahead of it. Velocity
        ///     resets to zero on a fresh target, a teleport, or when there is no target, so
        ///     re-acquisitions never produce a lead spike. When the lead is disabled the whole
        ///     computation is skipped and the offset is zero (no per-frame velocity cost).
        /// </summary>
        private void UpdatePursuitLead(
            in EyeSolveInput input,
            GazeChainCalibration chain,
            bool calibrated,
            in GazeReferenceFrame referenceFrame,
            float dt,
            out float leadDeltaYaw,
            out float leadDeltaPitch)
        {
            leadDeltaYaw = 0f;
            leadDeltaPitch = 0f;

            ConvaiGazeProfile profile = input.Profile;
            bool freshTarget = input.GenerationId != _lastGeneration || input.Teleported;

            if (profile.PursuitLeadSeconds <= 0f || !input.HasTarget || dt <= 0f)
            {
                _targetVelocity = Vector3.zero;
                _lastTargetPoint = input.TargetPoint;
                _hasLastTargetPoint = input.HasTarget;
                return;
            }

            if (!_hasLastTargetPoint || freshTarget)
            {
                // First frame on this target (or a cut): seed the position, no velocity yet.
                _targetVelocity = Vector3.zero;
                _lastTargetPoint = input.TargetPoint;
                _hasLastTargetPoint = true;
                return;
            }

            Vector3 rawVelocity = (input.TargetPoint - _lastTargetPoint) / dt;
            _lastTargetPoint = input.TargetPoint;

            float speedSq = rawVelocity.sqrMagnitude;
            if (speedSq > MaxTrackedTargetSpeed * MaxTrackedTargetSpeed)
                rawVelocity *= MaxTrackedTargetSpeed / Mathf.Sqrt(speedSq);

            float alpha = 1f - Mathf.Exp(-TargetVelocitySmoothing * dt);
            _targetVelocity += (rawVelocity - _targetVelocity) * alpha;

            Vector3 leadPoint = input.TargetPoint + _targetVelocity * profile.PursuitLeadSeconds;
            Vector3 eyeCenter = chain.EyeCenterPosition;
            float trueYaw = 0f;
            float truePitch = 0f;
            float leadYaw = 0f;
            float leadPitch = 0f;
            bool hasLeadAngles = calibrated
                ? GazeSolverMath.TryGetYawPitch(referenceFrame, eyeCenter, input.TargetPoint, out trueYaw, out truePitch) &&
                  GazeSolverMath.TryGetYawPitch(referenceFrame, eyeCenter, leadPoint, out leadYaw, out leadPitch)
                : GazeSolverMath.TryGetYawPitch(chain.Root, eyeCenter, input.TargetPoint, out trueYaw, out truePitch) &&
                  GazeSolverMath.TryGetYawPitch(chain.Root, eyeCenter, leadPoint, out leadYaw, out leadPitch);
            if (hasLeadAngles)
            {
                leadDeltaYaw = Mathf.DeltaAngle(trueYaw, leadYaw);
                leadDeltaPitch = leadPitch - truePitch;
            }
        }

        /// <summary>
        ///     Tracking sharpness, boosted while the goal would bring the eyes back toward orbit
        ///     centre. An eye returning to centre is unloading, not working, so it does so
        ///     faster than it went out — which is what makes "the head arrived and the eyes
        ///     settled" read as one movement rather than two.
        /// </summary>
        /// <remarks>
        ///     Applied in the fixation phase as well as in pursuit. It used to be pursuit-only,
        ///     which switched the boost off exactly where the return finishes: once the error
        ///     falls inside the pursuit band the state machine drops to fixation, and the last
        ///     few degrees back to centre — the ones a viewer actually reads — crawled.
        /// </remarks>
        private float RecenteringSharpness(ConvaiGazeProfile profile, float goalYaw)
        {
            bool returning = Mathf.Abs(goalYaw) < Mathf.Abs(_yaw);
            return returning
                ? profile.EyeTrackingSharpness * (1f + profile.OrbitRecenteringStrength)
                : profile.EyeTrackingSharpness;
        }

        private void StartSaccade(ConvaiGazeProfile profile, float endYaw, float endPitch, float amplitude)
        {
            _phase = EyePhase.Saccade;
            _saccadeStartYaw = _yaw;
            _saccadeStartPitch = _pitch;
            _saccadeEndYaw = _yaw + (endYaw - _yaw) * SaccadeUndershootFraction;
            _saccadeEndPitch = _pitch + (endPitch - _pitch) * SaccadeUndershootFraction;
            _saccadeTime = 0f;
            _saccadeDuration = profile.SaccadeMinDurationSeconds +
                               profile.SaccadeDurationPerDegree * amplitude;
            SaccadeStartedAmplitude = amplitude;
        }

        private void SolveVergence(
            in EyeSolveInput input,
            GazeChainCalibration chain,
            bool calibrated,
            in GazeReferenceFrame referenceFrame,
            ConvaiGazeProfile profile,
            float dt)
        {
            float targetLeft = 0f;
            float targetRight = 0f;

            // Vergence needs eye positions. Bone rigs read them from the eye transforms;
            // blendshape-only rigs synthesize a pair straddling the head, so both backends
            // converge on near targets.
            bool canVerge = chain.HasEyeBones || input.LookShapesActive;
            if (profile.EnableVergence && input.HasTarget && input.Engagement > 0.0001f && canVerge)
            {
                Vector3 eyeCenter = chain.EyeCenterPosition;
                Vector3 toTarget = input.TargetPoint - eyeCenter;
                float distance = toTarget.magnitude;

                Vector3 point = input.TargetPoint;
                if (distance < profile.VergenceMinDistance && distance > 1e-4f)
                    point = eyeCenter + toTarget * (profile.VergenceMinDistance / distance);

                // Vergence is the per-eye deviation from the CYCLOPEAN aim at the same
                // point — never from the live conjugate angle. Referencing the in-flight
                // _yaw here would leak the remaining saccade error into the vergence
                // channel and let both eyes leap ahead of the ballistic flight.
                bool hasCyclopeanYaw = calibrated
                    ? GazeSolverMath.TryGetYawPitch(referenceFrame, eyeCenter, point, out float cyclopeanYaw, out _)
                    : GazeSolverMath.TryGetYawPitch(chain.Root, eyeCenter, point, out cyclopeanYaw, out _);
                if (hasCyclopeanYaw)
                {
                    GetEyePositions(chain, calibrated, referenceFrame, profile, eyeCenter, out Vector3 leftPos, out Vector3 rightPos);
                    targetLeft = calibrated
                        ? PerEyeVergence(leftPos, referenceFrame, point, cyclopeanYaw, profile, input.Engagement)
                        : PerEyeVergence(leftPos, chain.Root, point, cyclopeanYaw, profile, input.Engagement);
                    targetRight = calibrated
                        ? PerEyeVergence(rightPos, referenceFrame, point, cyclopeanYaw, profile, input.Engagement)
                        : PerEyeVergence(rightPos, chain.Root, point, cyclopeanYaw, profile, input.Engagement);
                }
            }

            float alpha = 1f - Mathf.Exp(-VergenceSharpness * dt);
            _leftVergence += (targetLeft - _leftVergence) * alpha;
            _rightVergence += (targetRight - _rightVergence) * alpha;
        }

        /// <summary>
        ///     World positions of the two eyes for the vergence math: the actual eye bones
        ///     when present, otherwise a synthetic pair straddling <paramref name="eyeCenter" />
        ///     along the head's right axis at the profile interpupillary distance. The axis is
        ///     derived from the roll-immune eye-rest forward, so it follows head turns and does
        ///     not inherit authored bind-pose roll.
        /// </summary>
        private static void GetEyePositions(
            GazeChainCalibration chain,
            bool calibrated,
            in GazeReferenceFrame referenceFrame,
            ConvaiGazeProfile profile,
            Vector3 eyeCenter,
            out Vector3 leftPosition,
            out Vector3 rightPosition)
        {
            if (chain.HasEyeBones)
            {
                leftPosition = chain.LeftEye.position;
                rightPosition = chain.RightEye.position;
                return;
            }

            Vector3 forward = chain.CurrentEyeRestForward;
            if (forward.sqrMagnitude < 1e-6f)
                forward = chain.Root != null ? chain.Root.forward : Vector3.forward;

            Vector3 up = calibrated ? referenceFrame.Up : chain.Root != null ? chain.Root.up : Vector3.up;
            Vector3 right = Vector3.Cross(up, forward);
            if (right.sqrMagnitude < 1e-6f)
                right = calibrated ? referenceFrame.Right : chain.Root != null ? chain.Root.right : Vector3.right;
            right.Normalize();

            float half = Mathf.Max(0f, profile.SyntheticInterpupillaryDistance) * 0.5f;
            leftPosition = eyeCenter - right * half;   // character's left
            rightPosition = eyeCenter + right * half;  // character's right
        }

        private static float PerEyeVergence(
            Vector3 eyePosition,
            Transform root,
            Vector3 targetPoint,
            float cyclopeanYaw,
            ConvaiGazeProfile profile,
            float engagement)
        {
            if (!GazeSolverMath.TryGetYawPitch(root, eyePosition, targetPoint, out float eyeYawAbs, out _))
                return 0f;

            float offset = Mathf.DeltaAngle(cyclopeanYaw, eyeYawAbs) * Mathf.Clamp01(engagement);
            return Mathf.Clamp(offset, -profile.MaxConvergenceDegrees, profile.MaxConvergenceDegrees);
        }

        private static float PerEyeVergence(
            Vector3 eyePosition,
            in GazeReferenceFrame referenceFrame,
            Vector3 targetPoint,
            float cyclopeanYaw,
            ConvaiGazeProfile profile,
            float engagement)
        {
            if (!GazeSolverMath.TryGetYawPitch(referenceFrame, eyePosition, targetPoint, out float eyeYawAbs, out _))
                return 0f;

            float offset = Mathf.DeltaAngle(cyclopeanYaw, eyeYawAbs) * Mathf.Clamp01(engagement);
            return Mathf.Clamp(offset, -profile.MaxConvergenceDegrees, profile.MaxConvergenceDegrees);
        }

        private static float ClampPitch(float pitch, ConvaiGazeProfile profile)
        {
            return pitch >= 0f
                ? GazeSolverMath.SoftClamp(pitch, profile.EyeMaxPitchUpDegrees, profile.EyeSoftLimitFraction)
                : GazeSolverMath.SoftClamp(pitch, profile.EyeMaxPitchDownDegrees, profile.EyeSoftLimitFraction);
        }

        private static void ApplyEye(
            Transform eye,
            Transform root,
            Vector3 restForward,
            float restYaw,
            float restPitch,
            Vector2 orbitAngles)
        {
            if (eye == null) return;

            Vector3 desiredDir = GazeSolverMath.DirectionFromYawPitch(
                root, restYaw + orbitAngles.x, restPitch + orbitAngles.y);
            Quaternion swing = Quaternion.FromToRotation(restForward, desiredDir);
            eye.rotation = swing * eye.rotation;
        }

        private static void ApplyEye(
            Transform eye,
            in GazeReferenceFrame referenceFrame,
            Vector3 restForward,
            float restYaw,
            float restPitch,
            Vector2 orbitAngles)
        {
            if (eye == null) return;
            Vector3 desiredDir = GazeSolverMath.DirectionFromYawPitch(
                referenceFrame, restYaw + orbitAngles.x, restPitch + orbitAngles.y);
            eye.rotation = Quaternion.FromToRotation(restForward, desiredDir) * eye.rotation;
        }
    }
}
