using Convai.Runtime.Animation.ProceduralPose;
using Convai.Runtime.Embodiment;
using Convai.Domain.Embodiment.Interfaces;
using UnityEngine;

namespace Convai.Modules.BodyLanguage.Core.Pose
{
    /// <summary>
    ///     Idle-life micro-motion for the wrists and finger proximal phalanges:
    ///     a slow, band-limited two-frequency curl/roll so held-still hands never read as dead
    ///     rigid props between authored gestures. Purely additive and swing-only, same recipe
    ///     as <see cref="BreathSolver" />'s waveform composition — sum of two sines at different
    ///     rates, normalized to roughly ±1 by construction (no clamp needed).
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Owns its OWN <see cref="AnimatedAdditivePoseGuard" /> instance rather than sharing
    ///         <c>ProceduralPoseCompositor</c>'s guard: fingers/wrists are bones the compositor
    ///         never touches (it stays scoped to the spine/shoulder/head chain plus the pelvis
    ///         and legs), so this is a second, independent single-writer set — mirroring how
    ///         Gaze keeps its own guard for the eyes, an exclusive bone set none of the other
    ///         Runtime-shared guards ever write.
    ///     </para>
    ///     <para>
    ///         Deterministic: every finger and wrist gets its own phase, seeded once at
    ///         <see cref="Bind" /> from <see cref="DeterministicEmbodimentRandom" /> (a fixed
    ///         salt) so the same rig always produces the same micro-motion — a sum of two
    ///         low-frequency sines is band-limited by construction, so no additional filtering is
    ///         needed to keep this subtle.
    ///     </para>
    /// </remarks>
    internal sealed class HandMicroSolver
    {
        private const int FingerCount = 8; // Left Index/Middle/Ring/Little, then Right Index/Middle/Ring/Little.

        private const float FingerPrimaryRate = 0.35f;
        private const float FingerSecondaryRate = 0.13f;
        private const float WristPrimaryRate = 0.22f;
        private const float WristSecondaryRate = 0.09f;
        private const float SecondaryAmplitudeScale = 0.5f;
        private const float SecondaryPhaseScale = 1.7f;
        private const float NormalizeScale = 0.667f;

        /// <summary>Fixed salt so the same rig always seeds the same finger/wrist phases.</summary>
        private const uint PhaseSeedSalt = 0xF146E25u;

        private readonly AnimatedAdditivePoseGuard _guard = new();
        private readonly Transform[] _fingers = new Transform[FingerCount];
        private readonly float[] _fingerPhase = new float[FingerCount];

        private Transform _leftWrist;
        private Transform _rightWrist;
        private Transform _leftUpperArm;
        private Transform _rightUpperArm;
        private Transform _leftLowerArm;
        private Transform _rightLowerArm;
        private float _leftWristPhase;
        private float _rightWristPhase;
        private GestureCueKind _gestureKind;
        private float _gestureIntensity;
        private float _gestureElapsed;
        private float _gestureDuration;
        private bool _gestureUsesRight;

        /// <summary>Peak finger-proximal curl (degrees) at full weight — set by the caller from the profile each tick.</summary>
        public float MaxFingerCurlDegrees { get; set; } = 2.5f;

        /// <summary>Peak wrist micro-motion (degrees) at full weight — set by the caller from the profile each tick.</summary>
        public float MaxWristMicroDegrees { get; set; } = 2f;

        /// <summary>Profile-authored multiplier for semantic fallback poses.</summary>
        public float GestureAmplitudeScale { get; set; } = 1f;

        /// <summary>Whether both wrists resolved (the minimum bind requirement).</summary>
        public bool IsBound { get; private set; }

        /// <summary>Whether all eight proximal finger phalanges resolved. When <c>false</c>, bound wrists still animate (finger-degrade-to-wrist-only).</summary>
        public bool HasFingers { get; private set; }

        /// <summary>Whether the complete bilateral upper-arm/lower-arm/hand fallback chain resolved.</summary>
        public bool HasArmChain => IsBound && _leftUpperArm != null && _rightUpperArm != null &&
                                   _leftLowerArm != null && _rightLowerArm != null;

        /// <summary>Whether a procedural semantic fallback currently owns the arm/hand chain.</summary>
        public bool IsGestureActive => _gestureKind != GestureCueKind.None;

        /// <summary>
        ///     Resolves both wrists and the eight proximal finger phalanges from
        ///     <paramref name="animator" />'s Humanoid avatar and reseeds every phase. Thumbs are
        ///     deliberately excluded — a generic curl reads wrong on the thumb's opposition axis.
        ///     A <c>null</c> animator (or a non-Humanoid one) unbinds.
        /// </summary>
        public void Bind(Animator animator)
        {
            _guard.RestoreStaleWrites();

            if (animator == null || !animator.isHuman)
            {
                _leftWrist = null;
                _rightWrist = null;
                _leftUpperArm = null;
                _rightUpperArm = null;
                _leftLowerArm = null;
                _rightLowerArm = null;
                for (int i = 0; i < FingerCount; i++) _fingers[i] = null;
                IsBound = false;
                HasFingers = false;
                return;
            }

            _leftWrist = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            _rightWrist = animator.GetBoneTransform(HumanBodyBones.RightHand);
            _leftUpperArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            _rightUpperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            _leftLowerArm = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            _rightLowerArm = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);

            _fingers[0] = animator.GetBoneTransform(HumanBodyBones.LeftIndexProximal);
            _fingers[1] = animator.GetBoneTransform(HumanBodyBones.LeftMiddleProximal);
            _fingers[2] = animator.GetBoneTransform(HumanBodyBones.LeftRingProximal);
            _fingers[3] = animator.GetBoneTransform(HumanBodyBones.LeftLittleProximal);
            _fingers[4] = animator.GetBoneTransform(HumanBodyBones.RightIndexProximal);
            _fingers[5] = animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
            _fingers[6] = animator.GetBoneTransform(HumanBodyBones.RightRingProximal);
            _fingers[7] = animator.GetBoneTransform(HumanBodyBones.RightLittleProximal);

            IsBound = _leftWrist != null && _rightWrist != null;

            HasFingers = true;
            for (int i = 0; i < FingerCount; i++)
                HasFingers &= _fingers[i] != null;

            var random = new DeterministicEmbodimentRandom(DeterministicEmbodimentRandom.CreateSeed(animator, PhaseSeedSalt));
            for (int i = 0; i < FingerCount; i++)
                _fingerPhase[i] = random.Range(0f, Mathf.PI * 2f);
            _leftWristPhase = random.Range(0f, Mathf.PI * 2f);
            _rightWristPhase = random.Range(0f, Mathf.PI * 2f);
            _gestureUsesRight = random.Value >= 0.5f;
        }

        /// <summary>
        ///     Starts a conservative procedural gesture when no authored performer accepted the
        ///     semantic cue. The program is intentionally pose-relative and short: it adds a
        ///     readable conversational silhouette without pretending to be target-reaching IK.
        /// </summary>
        public bool TryTriggerGesture(GestureCueKind kind, float intensity)
        {
            if (!IsBound || _leftUpperArm == null || _rightUpperArm == null ||
                _leftLowerArm == null || _rightLowerArm == null || kind == GestureCueKind.None)
                return false;

            // Do not pop an active semantic program for a lower/equal priority request.
            if (IsGestureActive && kind != GestureCueKind.Emphatic) return false;

            _gestureKind = kind;
            _gestureIntensity = Mathf.Clamp01(intensity);
            _gestureElapsed = 0f;
            _gestureDuration = kind switch
            {
                GestureCueKind.Greeting => 1.35f,
                GestureCueKind.HandToChest => 1.45f,
                GestureCueKind.PalmToPlayer => 1.2f,
                GestureCueKind.IndicateObject => 1.1f,
                GestureCueKind.Uncertain => 1.25f,
                _ => 0.82f
            };
            _gestureUsesRight = !_gestureUsesRight;
            return true;
        }

        /// <summary>
        ///     Advances and writes this frame's micro-motion. Always restores last frame's stale
        ///     writes first (idempotent on a static rig); writes only when bound and
        ///     <paramref name="weight01" /> is above a tiny epsilon.
        /// </summary>
        /// <param name="time">Continuously increasing clock (seconds) driving the oscillators.</param>
        /// <param name="weight01">0..1 overall weight for this tick (already state/occupancy-gated by the caller).</param>
        /// <param name="dt">Unused by the oscillators themselves (phase is a function of <paramref name="time" />); kept for signature symmetry with the other pose solvers.</param>
        public void Tick(float time, float weight01, float dt)
        {
            _guard.RestoreStaleWrites();

            if (!IsBound) return;

            if (IsGestureActive)
            {
                TickGesture(dt, Mathf.Clamp01(weight01));
                return;
            }

            if (weight01 <= 1e-3f) return;

            float weight = Mathf.Clamp01(weight01);

            for (int i = 0; i < FingerCount; i++)
            {
                Transform bone = _fingers[i];
                if (bone == null) continue;

                float phase = _fingerPhase[i];
                float curl = (Mathf.Sin(time * FingerPrimaryRate + phase) +
                              SecondaryAmplitudeScale * Mathf.Sin(time * FingerSecondaryRate + phase * SecondaryPhaseScale)) *
                             NormalizeScale;
                ApplySwing(bone, curl * MaxFingerCurlDegrees * weight);
            }

            float leftWristCurl = (Mathf.Sin(time * WristPrimaryRate + _leftWristPhase) +
                                    SecondaryAmplitudeScale * Mathf.Sin(time * WristSecondaryRate + _leftWristPhase * SecondaryPhaseScale)) *
                                   NormalizeScale;
            ApplySwing(_leftWrist, leftWristCurl * MaxWristMicroDegrees * weight);

            float rightWristCurl = (Mathf.Sin(time * WristPrimaryRate + _rightWristPhase) +
                                     SecondaryAmplitudeScale * Mathf.Sin(time * WristSecondaryRate + _rightWristPhase * SecondaryPhaseScale)) *
                                    NormalizeScale;
            ApplySwing(_rightWrist, rightWristCurl * MaxWristMicroDegrees * weight);
        }

        private void TickGesture(float dt, float masterWeight)
        {
            _gestureElapsed += Mathf.Max(0f, dt);
            float t = Mathf.Clamp01(_gestureElapsed / Mathf.Max(0.01f, _gestureDuration));
            float envelope = GestureEnvelope(t) * _gestureIntensity * masterWeight *
                             Mathf.Clamp(GestureAmplitudeScale, 0.25f, 1.5f);

            float upperSagittal = 0f;
            float upperLateral = 0f;
            float elbowSagittal = 0f;
            float wristSagittal = 0f;
            bool bilateral = false;

            switch (_gestureKind)
            {
                case GestureCueKind.Greeting:
                    upperSagittal = -18f; upperLateral = 14f; elbowSagittal = -32f; wristSagittal = 10f;
                    break;
                case GestureCueKind.HandToChest:
                    upperSagittal = -24f; upperLateral = -12f; elbowSagittal = -48f; wristSagittal = -8f;
                    break;
                case GestureCueKind.PalmToPlayer:
                case GestureCueKind.IndicateObject:
                    upperSagittal = -20f; upperLateral = 8f; elbowSagittal = -24f; wristSagittal = 8f;
                    break;
                case GestureCueKind.Uncertain:
                    upperSagittal = -8f; upperLateral = 12f; elbowSagittal = -18f; wristSagittal = 9f; bilateral = true;
                    break;
                case GestureCueKind.Negative:
                    upperSagittal = -7f; upperLateral = 8f; elbowSagittal = -14f; wristSagittal = -6f; bilateral = true;
                    break;
                case GestureCueKind.Emphatic:
                    upperSagittal = -13f; upperLateral = 5f; elbowSagittal = -20f; bilateral = true;
                    break;
                default:
                    upperSagittal = -9f; upperLateral = 4f; elbowSagittal = -15f;
                    break;
            }

            if (bilateral || !_gestureUsesRight)
                ApplyArmGesture(_leftUpperArm, _leftLowerArm, _leftWrist,
                    upperSagittal, -upperLateral, elbowSagittal, wristSagittal, envelope);
            if (bilateral || _gestureUsesRight)
                ApplyArmGesture(_rightUpperArm, _rightLowerArm, _rightWrist,
                    upperSagittal, upperLateral, elbowSagittal, wristSagittal, envelope);

            if (t >= 1f)
            {
                _gestureKind = GestureCueKind.None;
                _gestureElapsed = 0f;
            }
        }

        private void ApplyArmGesture(
            Transform upperArm, Transform lowerArm, Transform wrist,
            float upperSagittal, float upperLateral, float elbowSagittal,
            float wristSagittal, float weight)
        {
            ApplySwing(upperArm, upperSagittal * weight, upperLateral * weight);
            ApplySwing(lowerArm, elbowSagittal * weight, 0f);
            ApplySwing(wrist, wristSagittal * weight, 0f);
        }

        private static float GestureEnvelope(float t)
        {
            // 28% preparation, 34% stroke/hold, 38% retraction. Every boundary has zero
            // velocity, so rejected clips never become a visible arm pop.
            if (t < 0.28f) return SmootherStep(t / 0.28f);
            if (t < 0.62f) return 1f;
            return 1f - SmootherStep((t - 0.62f) / 0.38f);
        }

        private static float SmootherStep(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * t * (t * (t * 6f - 15f) + 10f);
        }

        /// <summary>Restores any residual write and clears the bound rig — call on disable and before a rebind.</summary>
        public void Reset()
        {
            _guard.RestoreStaleWrites();
            _leftWrist = null;
            _rightWrist = null;
            _leftUpperArm = null;
            _rightUpperArm = null;
            _leftLowerArm = null;
            _rightLowerArm = null;
            for (int i = 0; i < FingerCount; i++) _fingers[i] = null;
            IsBound = false;
            HasFingers = false;
            _gestureKind = GestureCueKind.None;
            _gestureElapsed = 0f;
        }

        private void ApplySwing(Transform bone, float sagittalDegrees, float lateralDegrees = 0f)
        {
            if (bone == null) return;
            if (Mathf.Abs(sagittalDegrees) < 1e-4f) return;

            Transform reference = bone.parent != null ? bone.parent : bone;
            Quaternion preWrite = bone.localRotation;
            Quaternion delta = ProceduralPoseMath.SwingDelta(reference, sagittalDegrees, lateralDegrees);
            if (delta == Quaternion.identity) return;

            ProceduralPoseMath.ApplyWorldSwing(bone, delta);
            _guard.Record(bone, preWrite);
        }
    }
}
