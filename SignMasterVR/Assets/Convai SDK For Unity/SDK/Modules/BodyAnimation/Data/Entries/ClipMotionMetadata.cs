using System;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Data
{
    public enum MotionFootSide
    {
        Unknown = 0,
        Left = 1,
        Right = 2
    }

    /// <summary>
    ///     Measured motion characteristics of an in-place locomotion clip. Because these
    ///     clips carry no root motion, the runtime needs this data to keep the NavMeshAgent
    ///     and the animation in lockstep: playback-rate warping uses
    ///     <see cref="AuthoredSpeed" />, distance-matched stops use
    ///     <see cref="AuthoredDistance" /> / <see cref="DistanceCurve" />, scripted turn
    ///     rotation uses <see cref="AuthoredYawDegrees" /> / <see cref="YawCurve" />, and
    ///     planted transitions use the foot-plant markers.
    /// </summary>
    /// <remarks>
    ///     Values are produced by the Clip Motion Analyzer editor tool (internal tooling; see
    ///     <c>Convai → Body Animation</c> menu) and can be overridden by hand afterwards.
    ///     Unanalyzed metadata (<see cref="IsAnalyzed" /> false) makes the runtime fall back
    ///     to simple crossfades for the affected feature and log the degradation once.
    /// </remarks>
    [Serializable]
    public sealed class ClipMotionMetadata
    {
        public const int CurrentSchemaVersion = 3;

        [SerializeField, HideInInspector] private int _schemaVersion = CurrentSchemaVersion;
        [SerializeField, Range(0f, 1f)] private float _recommendedHandoffNormalizedTime = 0.9f;
        [SerializeField, Range(0f, 1f)] private float _recoveryNormalizedTime = 0.95f;
        [SerializeField, Range(0f, 1f)] private float _primaryPivotPlantNormalizedTime;
        [SerializeField] private MotionFootSide _primaryPivotFoot = MotionFootSide.Unknown;
        [SerializeField, Range(0f, 1f)] private float _loopClosureQuality;
        [SerializeField]
        [Min(0f)]
        [Tooltip("Average forward speed (m/s) the clip was authored at. 0 = unknown. " +
                 "Used for playback-rate warping on movement loops.")]
        private float _authoredSpeed;

        [SerializeField]
        [Min(0f)]
        [Tooltip("Total forward distance (meters) the motion covers. Used by starts and " +
                 "stops for distance matching. 0 = unknown.")]
        private float _authoredDistance;

        [SerializeField]
        [Tooltip("Total signed yaw (degrees, +right/−left) the motion turns through. " +
                 "Used by turn-in-place and directional starts. 0 = none.")]
        private float _authoredYawDegrees;

        [SerializeField]
        [Tooltip("Normalized time → meters traveled. Drives agent speed during starts/stops " +
                 "so capsule displacement matches the animation.")]
        private AnimationCurve _distanceCurve = new();

        [SerializeField]
        [Tooltip("Normalized time → signed degrees turned. Drives scripted root rotation " +
                 "during turns and directional starts.")]
        private AnimationCurve _yawCurve = new();

        [SerializeField]
        [Tooltip("Normalized times where the LEFT foot plants (weight-bearing contact begins).")]
        private float[] _leftFootPlants = Array.Empty<float>();

        [SerializeField]
        [Tooltip("Normalized times where the RIGHT foot plants (weight-bearing contact begins).")]
        private float[] _rightFootPlants = Array.Empty<float>();

        [SerializeField]
        [Tooltip("Set by the Clip Motion Analyzer. Hand-tuned values survive re-analysis " +
                 "only when re-run is confirmed.")]
        private bool _analyzed;

        [SerializeField]
        [Min(0f)]
        [Tooltip("The sample rig's combined motion scale at analysis time (0 = unknown, " +
                 "schema v2 or earlier). NOT a user setting — it records the size of the rig " +
                 "the Clip Motion Analyzer measured this clip on, so the runtime can tell how " +
                 "much bigger or smaller the character it is actually driving is and rescale " +
                 "the authored speeds/distances to match. It is deliberately the sample rig's " +
                 "Animator.humanScale combined with the uniform component of that rig " +
                 "instance's world lossyScale, not humanScale alone: the analyzer measures foot " +
                 "positions in WORLD space on an instantiated sample rig, so the metres it " +
                 "recorded already include that instance's transform scale — the normalizing " +
                 "quantity has to include both factors to invert what was actually measured.")]
        private float _authoredMotionScale;

        public float AuthoredSpeed => _authoredSpeed;
        public float AuthoredDistance => _authoredDistance;
        public float AuthoredYawDegrees => _authoredYawDegrees;
        public AnimationCurve DistanceCurve => _distanceCurve;
        public AnimationCurve YawCurve => _yawCurve;
        public float[] LeftFootPlants => _leftFootPlants;
        public float[] RightFootPlants => _rightFootPlants;
        public bool IsAnalyzed => _analyzed;
        public int SchemaVersion => _schemaVersion;
        public float RecommendedHandoffNormalizedTime => Mathf.Clamp01(_recommendedHandoffNormalizedTime);
        public float RecoveryNormalizedTime => Mathf.Max(RecommendedHandoffNormalizedTime, Mathf.Clamp01(_recoveryNormalizedTime));
        public float PrimaryPivotPlantNormalizedTime => Mathf.Clamp01(_primaryPivotPlantNormalizedTime);
        public MotionFootSide PrimaryPivotFoot => _primaryPivotFoot;
        public float LoopClosureQuality => Mathf.Clamp01(_loopClosureQuality);

        /// <summary>The sample rig's combined motion scale at analysis time. See the field's XML docs above.</summary>
        public float AuthoredMotionScale => _authoredMotionScale;

        /// <summary>True when this metadata records the sample rig's motion scale (schema v3+, analyzed).</summary>
        public bool HasAuthoredMotionScale => _authoredMotionScale > 0f;

        public bool HasSpeed => _authoredSpeed > 0f;
        public bool HasDistance => _authoredDistance > 0f && _distanceCurve != null && _distanceCurve.length >= 2;
        public bool HasYaw => Mathf.Abs(_authoredYawDegrees) > 0.5f && _yawCurve != null && _yawCurve.length >= 2;
        public bool HasFootPlants => (_leftFootPlants?.Length ?? 0) + (_rightFootPlants?.Length ?? 0) > 0;

        /// <summary>Meters traveled at the given normalized time (distance-matched stops/starts).</summary>
        public float EvaluateDistance(float normalizedTime) =>
            HasDistance ? Mathf.Max(0f, _distanceCurve.Evaluate(Mathf.Clamp01(normalizedTime))) : 0f;

        /// <summary>Signed degrees turned at the given normalized time (scripted rotation).</summary>
        public float EvaluateYaw(float normalizedTime) =>
            HasYaw ? _yawCurve.Evaluate(Mathf.Clamp01(normalizedTime)) : 0f;

        /// <summary>Writer used by the Clip Motion Analyzer and tests.</summary>
        internal void SetAnalyzed(
            float authoredSpeed,
            float authoredDistance,
            float authoredYawDegrees,
            AnimationCurve distanceCurve,
            AnimationCurve yawCurve,
            float[] leftFootPlants,
            float[] rightFootPlants,
            float recommendedHandoffNormalizedTime = 0.9f,
            float recoveryNormalizedTime = 0.95f,
            MotionFootSide primaryPivotFoot = MotionFootSide.Unknown,
            float primaryPivotPlantNormalizedTime = 0f,
            float loopClosureQuality = 0f,
            float authoredMotionScale = 0f)
        {
            _authoredSpeed = Mathf.Max(0f, authoredSpeed);
            _authoredDistance = Mathf.Max(0f, authoredDistance);
            _authoredYawDegrees = authoredYawDegrees;
            _distanceCurve = distanceCurve ?? new AnimationCurve();
            _yawCurve = yawCurve ?? new AnimationCurve();
            _leftFootPlants = leftFootPlants ?? Array.Empty<float>();
            _rightFootPlants = rightFootPlants ?? Array.Empty<float>();
            _recommendedHandoffNormalizedTime = Mathf.Clamp01(recommendedHandoffNormalizedTime);
            _recoveryNormalizedTime = Mathf.Clamp(recoveryNormalizedTime, _recommendedHandoffNormalizedTime, 1f);
            _primaryPivotFoot = primaryPivotFoot;
            _primaryPivotPlantNormalizedTime = Mathf.Clamp01(primaryPivotPlantNormalizedTime);
            _loopClosureQuality = Mathf.Clamp01(loopClosureQuality);
            _authoredMotionScale = Mathf.Max(0f, authoredMotionScale);
            _schemaVersion = CurrentSchemaVersion;
            _analyzed = true;
        }
    }
}
