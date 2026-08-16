using Convai.Runtime.Animation.ProceduralPose;
using UnityEngine;

namespace Convai.Modules.BodyLanguage.Core.Pose
{
    /// <summary>Per-frame input for <see cref="PostureSolver.Solve" />.</summary>
    internal struct PostureSolveInput
    {
        public float DeltaTime;

        /// <summary>Openness target, -1 (closed/rounded) .. 1 (open/lifted). Sustained (state + emotion) only.</summary>
        public float OpennessTarget;

        /// <summary>
        ///     Sustained sagittal lean target, -1 (retract) .. 1 (lean toward interlocutor) —
        ///     the state-policy + emotion bias from <c>PostureDirector</c>. Combines with
        ///     <see cref="TransientLeanTarget" /> (clamped together) before scaling to degrees;
        ///     see <see cref="LeanSustainFloor" /> for how it survives suppression.
        /// </summary>
        public float SustainedLeanTarget;

        /// <summary>
        ///     Transient sagittal lean contribution, -1..1 — posture-pulse beats and the
        ///     listening lean-in bias. Ducks fully under suppression (weighted by
        ///     <see cref="SuppressionWeight" /> only, no floor).
        /// </summary>
        public float TransientLeanTarget;

        /// <summary>Shoulder tension target, -1 (dropped/relaxed) .. 1 (raised/tense). Sustained (emotion) only.</summary>
        public float TensionTarget;

        /// <summary>
        ///     Lateral weight-shift / side-bend target, -1..1. Sign convention: <b>positive
        ///     shifts weight/side-bends toward the character's own right</b> (mirrors the
        ///     existing shoulder-tension convention of positive-right, negative-left). Swing-only
        ///     (side-bend about the spine chain's forward axis) — never roll/twist, and never
        ///     touches the shoulders (their lateral axis is tension, not weight shift). Entirely
        ///     transient — ducks fully under suppression, no sustain floor.
        /// </summary>
        public float LateralShiftTarget;

        /// <summary>
        ///     Master weight 0..1 — fade/enable only (enable ramp, disable/FullBody-suppression
        ///     fade). NEVER carries transient (UpperBody) suppression; see
        ///     <see cref="SuppressionWeight" /> for that. At (or asymptotically toward) 0 the
        ///     solver's solved degrees settle at zero for ANY channel, sustained included — this
        ///     is the one weight that can zero out the sustained silhouette (FullBody
        ///     suppression, or disable).
        /// </summary>
        public float MasterWeight;

        /// <summary>
        ///     Transient (UpperBody) suppression weight, 0..1 — already slewed by the caller.
        ///     Fully weights every transient channel (<see cref="TransientLeanTarget" />,
        ///     <see cref="LateralShiftTarget" />) and floors the sustained channels'
        ///     effective weight at their respective Sustain-floor consts
        ///     (<c>Max(SuppressionWeight, floor)</c>) so the sustained silhouette survives
        ///     UpperBody speech-suppression instead of ducking with the transient motion.
        /// </summary>
        public float SuppressionWeight;

        /// <summary>
        ///     Floor on <see cref="OpennessTarget" />'s effective weight under suppression —
        ///     conservative, feel-tunable; sustained silhouette survives UpperBody
        ///     speech-suppression; NOT a public profile knob (minimize surface).
        /// </summary>
        public float OpennessSustainFloor;

        /// <summary>
        ///     Floor on <see cref="SustainedLeanTarget" />'s effective weight under suppression —
        ///     conservative, feel-tunable; sustained silhouette survives UpperBody
        ///     speech-suppression; NOT a public profile knob (minimize surface).
        /// </summary>
        public float LeanSustainFloor;

        /// <summary>
        ///     Floor on <see cref="TensionTarget" />'s effective weight under suppression —
        ///     conservative, feel-tunable; sustained silhouette survives UpperBody
        ///     speech-suppression; NOT a public profile knob (minimize surface).
        /// </summary>
        public float TensionSustainFloor;

        public float MaxOpennessDegrees;
        public float MaxLeanDegrees;
        public float MaxTensionDegrees;

        /// <summary>Spine rotation (degrees) that a full ±1 <see cref="LateralShiftTarget" /> maps to.</summary>
        public float MaxLateralShiftDegrees;

        public float SpringSharpness;
        public float MaxAngularSpeedDegreesPerSecond;
    }

    /// <summary>
    ///     Additive, swing-only posture signal shaper: spine-chain openness/lean + shoulder
    ///     tension, spring-damped toward director-supplied targets
    ///     (<c>PostureDirector</c> is the target source). This solver is a pure signal shaper: it
    ///     never touches a <see cref="Transform" />. <see cref="SpineSagittalDegrees" />,
    ///     <see cref="SpineLateralDegrees" />, and <see cref="ShoulderTensionDegrees" /> are the
    ///     shaped outputs; the owning controller's <c>ProceduralPoseCompositor</c> is the single
    ///     writer that turns them into bone deltas.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <see cref="PostureSolveInput.MasterWeight" /> reaching (numerically) zero is the
    ///         disable/suppression contract: the solved degrees spring toward zero and the
    ///         compositor stops writing once they cross its own write threshold. Callers fade
    ///         the weight down over the profile's fade time — this solver never sees a hard cut.
    ///     </para>
    /// </remarks>
    internal sealed class PostureSolver
    {
        private const float WriteEpsilonDegrees = 0.01f;

        // Springs operate in physical degree-space (matching the gaze solver chain's
        // recipe) so MaxAngularSpeedDegreesPerSecond is a genuine degrees/second clamp;
        // the normalized -1..1 targets are scaled to degrees before smoothing, and the
        // public Openness/Lean/Tension properties divide back down for the -1..1 contract
        // consumers (snapshot, tests) expect.
        private float _opennessDegrees;
        private float _leanDegrees;
        private float _tensionDegrees;
        private float _lateralDegrees;
        private float _opennessVelocity;
        private float _leanVelocity;
        private float _tensionVelocity;
        private float _lateralVelocity;
        private float _lastMaxOpennessDegrees = 1f;
        private float _lastMaxLeanDegrees = 1f;
        private float _lastMaxTensionDegrees = 1f;
        private float _lastMaxLateralShiftDegrees = 1f;
        private float _lastWeight;

        /// <summary>Solved openness value this tick, post-spring, normalized -1..1.</summary>
        public float Openness => SafeDivide(_opennessDegrees, _lastMaxOpennessDegrees);

        /// <summary>Solved sagittal lean value this tick, post-spring, normalized -1..1.</summary>
        public float Lean => SafeDivide(_leanDegrees, _lastMaxLeanDegrees);

        /// <summary>Solved shoulder tension value this tick, post-spring, normalized -1..1.</summary>
        public float Tension => SafeDivide(_tensionDegrees, _lastMaxTensionDegrees);

        /// <summary>
        ///     Solved lateral weight-shift value this tick, post-spring, normalized -1..1 (see
        ///     <see cref="PostureSolveInput.LateralShiftTarget" /> for the sign convention).
        /// </summary>
        public float LateralShift => SafeDivide(_lateralDegrees, _lastMaxLateralShiftDegrees);

        /// <summary>
        ///     Sagittal spine-chain swing (degrees) this tick — openness lifts/opens (negative)
        ///     composed with lean (positive toward the interlocutor). The shaped signal a
        ///     compositor writes; v1's <c>sagittalTotal</c>.
        /// </summary>
        public float SpineSagittalDegrees => -_opennessDegrees + _leanDegrees;

        /// <summary>Settled openness magnitude in degrees, for anatomical chain distribution.</summary>
        public float OpennessDegrees => _opennessDegrees;

        /// <summary>Settled sustained + transient lean magnitude in degrees.</summary>
        public float LeanDegrees => _leanDegrees;

        /// <summary>Lateral spine-chain swing (degrees) this tick — the shaped signal a compositor writes; v1's <c>lateralTotal</c>.</summary>
        public float SpineLateralDegrees => _lateralDegrees;

        /// <summary>Shoulder tension (degrees) this tick — LEFT gets +value, RIGHT gets -value, applied by the compositor.</summary>
        public float ShoulderTensionDegrees => _tensionDegrees;

        /// <summary>
        ///     Whether this tick's solved degrees are large enough to be worth writing — mirrors
        ///     v1's pre-Apply early-return condition (<c>weight&lt;=0 &amp;&amp; all
        ///     |values|&lt;WriteEpsilonDegrees</c>), now exposed for a consumer to decide
        ///     whether to write instead of gating the solve itself. The springs always run
        ///     regardless (identical numeric behavior to v1, which also ran springs before the
        ///     check) — only whether a write would be worthwhile is reported here.
        /// </summary>
        public bool HasMeaningfulOutput =>
            !(_lastWeight <= 0f &&
              Mathf.Abs(_opennessDegrees) < WriteEpsilonDegrees &&
              Mathf.Abs(_leanDegrees) < WriteEpsilonDegrees &&
              Mathf.Abs(_tensionDegrees) < WriteEpsilonDegrees &&
              Mathf.Abs(_lateralDegrees) < WriteEpsilonDegrees);

        public void Reset()
        {
            _opennessDegrees = 0f;
            _leanDegrees = 0f;
            _tensionDegrees = 0f;
            _lateralDegrees = 0f;
            _opennessVelocity = 0f;
            _leanVelocity = 0f;
            _tensionVelocity = 0f;
            _lateralVelocity = 0f;
            _lastWeight = 0f;
        }

        public void Solve(in PostureSolveInput input)
        {
            float dt = input.DeltaTime > 0f ? input.DeltaTime : 1f / 60f;
            float weight = Mathf.Clamp01(input.MasterWeight);
            _lastWeight = weight;

            // Two-weight split (sustained vs. transient posture sources): transientSupp is the
            // slewed UpperBody suppression factor, applied fully to every transient channel; each
            // sustained channel's EFFECTIVE weight is floored at that channel's own sustain-floor
            // const, so it survives suppression instead of ducking with the transient motion.
            // When SuppressionWeight == 1 (no suppression), Max(1, floor) == 1 for every floor —
            // this reduces bit-identically to the pre-split single-weight solve.
            float transientSupp = Mathf.Clamp01(input.SuppressionWeight);
            float opennessSustainWeight = Mathf.Max(transientSupp, input.OpennessSustainFloor);
            float leanSustainWeight = Mathf.Max(transientSupp, input.LeanSustainFloor);
            float tensionSustainWeight = Mathf.Max(transientSupp, input.TensionSustainFloor);

            _lastMaxOpennessDegrees = Mathf.Max(0.0001f, input.MaxOpennessDegrees);
            _lastMaxLeanDegrees = Mathf.Max(0.0001f, input.MaxLeanDegrees);
            _lastMaxTensionDegrees = Mathf.Max(0.0001f, input.MaxTensionDegrees);
            _lastMaxLateralShiftDegrees = Mathf.Max(0.0001f, input.MaxLateralShiftDegrees);

            float opennessGoalDegrees =
                Mathf.Clamp(input.OpennessTarget * opennessSustainWeight, -1f, 1f) * weight * _lastMaxOpennessDegrees;
            // Double-lean guard: sustained and transient lean are combined and clamped to -1..1
            // TOGETHER (mirrors the controller's own combined clamp today) before the master
            // weight and degree scale apply, so the two sources can never compound past a single
            // target's own -1..1 range.
            float leanGoalDegrees =
                Mathf.Clamp(input.SustainedLeanTarget * leanSustainWeight + input.TransientLeanTarget * transientSupp, -1f, 1f) *
                weight * _lastMaxLeanDegrees;
            float tensionGoalDegrees =
                Mathf.Clamp(input.TensionTarget * tensionSustainWeight, -1f, 1f) * weight * _lastMaxTensionDegrees;
            float lateralGoalDegrees =
                Mathf.Clamp(input.LateralShiftTarget * transientSupp, -1f, 1f) * weight * _lastMaxLateralShiftDegrees;

            _opennessDegrees = ProceduralPoseMath.SpringValue(_opennessDegrees, opennessGoalDegrees, ref _opennessVelocity,
                input.SpringSharpness, input.MaxAngularSpeedDegreesPerSecond, dt);
            _leanDegrees = ProceduralPoseMath.SpringValue(_leanDegrees, leanGoalDegrees, ref _leanVelocity,
                input.SpringSharpness, input.MaxAngularSpeedDegreesPerSecond, dt);
            _tensionDegrees = ProceduralPoseMath.SpringValue(_tensionDegrees, tensionGoalDegrees, ref _tensionVelocity,
                input.SpringSharpness, input.MaxAngularSpeedDegreesPerSecond, dt);
            _lateralDegrees = ProceduralPoseMath.SpringValue(_lateralDegrees, lateralGoalDegrees, ref _lateralVelocity,
                input.SpringSharpness, input.MaxAngularSpeedDegreesPerSecond, dt);
        }

        private static float SafeDivide(float degrees, float maxDegrees) =>
            maxDegrees > 0.0001f ? degrees / maxDegrees : 0f;
    }
}
