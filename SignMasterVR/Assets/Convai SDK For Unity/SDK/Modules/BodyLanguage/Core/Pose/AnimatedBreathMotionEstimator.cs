using UnityEngine;

namespace Convai.Modules.BodyLanguage.Core.Pose
{
    /// <summary>
    ///     Adaptive-layering estimator ("auto-duck"): watches the
    ///     ANIMATED pose's own torso rotation for a slow, low-frequency oscillation — an idle
    ///     clip that already breathes — and reports how large that baked motion is so the
    ///     procedural breath layer can duck itself underneath it instead of beating against it.
    ///     A pure signal shaper: it never touches a <see cref="Transform" />, only reads a
    ///     rotation sample the caller supplies (<see cref="Convai.Runtime.Animation.ProceduralPose.ProceduralPoseCompositor.AnimatedTorsoLocalRotation" />).
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Two Slerp-based one-pole filters chase the same sample at different speeds: a fast
    ///         low-pass (τ≈0.3s) that kills anything above roughly half a hertz — fast talk or
    ///         gesture motion never reads as breathing — and a slow baseline (τ≈6s) that tracks
    ///         posture drift. The angular distance between them is the instantaneous deviation a
    ///         slow oscillation would produce; an envelope follower (τ≈3s) smooths that into
    ///         <see cref="BakedAmplitudeDegrees" />, with a mean-to-peak fudge factor since the
    ///         envelope tracks a rectified mean, not the waveform's peak.
    ///     </para>
    ///     <para>
    ///         <see cref="DuckFactor" /> maps that amplitude through a small dead zone (below
    ///         which the baked motion is too subtle to fight) into a duck curve, floored so the
    ///         procedural breath is never fully silenced — phase continuity must survive even at
    ///         maximum duck, or the character's own breath resumes with a pop the moment the
    ///         animated pose's baked breathing weakens.
    ///     </para>
    ///     <para>
    ///         All constants below are internal and deliberately documented "feel-pass tunable" —
    ///         they are reasonable starting points, not a final calibration.
    ///     </para>
    /// </remarks>
    internal sealed class AnimatedBreathMotionEstimator
    {
        /// <summary>Low-pass time constant (seconds) — feel-pass tunable.</summary>
        private const float LowPassTauSeconds = 0.3f;

        /// <summary>Slow baseline time constant (seconds) — feel-pass tunable.</summary>
        private const float BaselineTauSeconds = 6f;

        /// <summary>Envelope-follower time constant (seconds) — feel-pass tunable.</summary>
        private const float EnvelopeTauSeconds = 3f;

        /// <summary>Mean-to-peak fudge factor applied to the smoothed envelope — feel-pass tunable.</summary>
        private const float EnvelopePeakFudge = 1.6f;

        /// <summary>Baked amplitude (degrees) below which the duck curve reports zero — feel-pass tunable.</summary>
        private const float DuckRangeLowDegrees = 0.25f;

        /// <summary>Baked amplitude (degrees) at which the duck curve saturates to 1 — feel-pass tunable.</summary>
        private const float DuckRangeHighDegrees = 2.0f;

        /// <summary>
        ///     Minimum <see cref="DuckFactor" /> even at full duck — the procedural breath is
        ///     never fully silenced, so its phase survives regardless of how strongly the
        ///     animated pose is already breathing — feel-pass tunable.
        /// </summary>
        private const float DuckFloor = 0.25f;

        private Quaternion _lowPass = Quaternion.identity;
        private Quaternion _baseline = Quaternion.identity;
        private float _envelope;
        private bool _initialized;

        /// <summary>Current estimate of the animated pose's own slow torso oscillation amplitude, in degrees.</summary>
        public float BakedAmplitudeDegrees { get; private set; }

        /// <summary>
        ///     Multiplier the procedural breath depth should be scaled by this tick — 1 means no
        ///     duck, <see cref="DuckFloor" /> is the floor at full baked breathing.
        /// </summary>
        public float DuckFactor { get; private set; } = 1f;

        /// <summary>Resets to the unestimated state: no baked motion assumed, no duck applied.</summary>
        public void Reset()
        {
            _lowPass = Quaternion.identity;
            _baseline = Quaternion.identity;
            _envelope = 0f;
            _initialized = false;
            BakedAmplitudeDegrees = 0f;
            DuckFactor = 1f;
        }

        /// <summary>
        ///     Advances the estimator by one tick. <paramref name="sampleValid" /> false (rig
        ///     unbound) freezes the filters exactly like <paramref name="stateDuckWeight" />
        ///     <c>&lt;= 0</c> does — a talking/reacting body (or a momentarily unbound rig) must
        ///     never pollute the estimate — while <see cref="DuckFactor" /> is still recomputed
        ///     from whatever the envelope is currently holding, weighted by
        ///     <paramref name="stateDuckWeight" /> (so a weight of exactly 0 always yields exactly
        ///     1, regardless of the frozen amplitude).
        /// </summary>
        public void Tick(Quaternion animatedTorsoLocalRotation, bool sampleValid, float stateDuckWeight, float deltaTime)
        {
            if (sampleValid)
            {
                if (!_initialized)
                {
                    _lowPass = animatedTorsoLocalRotation;
                    _baseline = animatedTorsoLocalRotation;
                    _envelope = 0f;
                    BakedAmplitudeDegrees = 0f;
                    _initialized = true;
                }
                else if (deltaTime > 0f && stateDuckWeight > 0f)
                {
                    _lowPass = Quaternion.Slerp(_lowPass, animatedTorsoLocalRotation, Alpha(deltaTime, LowPassTauSeconds));
                    _baseline = Quaternion.Slerp(_baseline, animatedTorsoLocalRotation, Alpha(deltaTime, BaselineTauSeconds));

                    float angle = Quaternion.Angle(_lowPass, _baseline);
                    _envelope += (angle - _envelope) * Alpha(deltaTime, EnvelopeTauSeconds);
                    BakedAmplitudeDegrees = _envelope * EnvelopePeakFudge;
                }
            }

            float duck01 = Mathf.Clamp01((BakedAmplitudeDegrees - DuckRangeLowDegrees) / (DuckRangeHighDegrees - DuckRangeLowDegrees));
            DuckFactor = 1f - Mathf.Clamp01(stateDuckWeight) * duck01 * (1f - DuckFloor);
        }

        private static float Alpha(float dt, float tau) => dt <= 0f ? 0f : 1f - Mathf.Exp(-dt / tau);
    }
}
