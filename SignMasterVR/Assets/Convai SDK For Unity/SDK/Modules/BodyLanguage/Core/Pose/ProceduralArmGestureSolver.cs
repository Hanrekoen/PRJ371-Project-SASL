using Convai.Domain.Embodiment.Interfaces;
using Convai.Domain.Embodiment.Readings;
using Convai.Runtime.Animation.ProceduralPose;
using UnityEngine;

namespace Convai.Modules.BodyLanguage.Core.Pose
{
    /// <summary>Single-writer, pose-relative co-speech arm motor with preparation/stroke/retraction timing.</summary>
    internal sealed class ProceduralArmGestureSolver
    {
        private readonly AnimatedAdditivePoseGuard _guard = new();
        private readonly Transform[] _leftFingers = new Transform[4];
        private readonly Transform[] _rightFingers = new Transform[4];
        private Transform _lu, _ll, _lh, _ru, _rl, _rh;
        private CoSpeechGestureRequest _request;
        private float _elapsed;

        public float AmplitudeScale { get; set; } = 1f;

        public bool IsActive => _request.Kind != GestureCueKind.None;

        public void Bind(Animator animator)
        {
            Reset();
            if (animator == null || !animator.isHuman) return;
            _lu = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            _ll = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            _lh = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            _ru = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            _rl = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            _rh = animator.GetBoneTransform(HumanBodyBones.RightHand);
            BindFingers(animator, true, _leftFingers);
            BindFingers(animator, false, _rightFingers);
        }

        public bool TryStart(in CoSpeechGestureRequest request)
        {
            if (_lu == null || _ll == null || _lh == null ||
                _ru == null || _rl == null || _rh == null || request.Kind == GestureCueKind.None) return false;
            if (IsActive && request.Sequence <= _request.Sequence) return false;
            _request = request;
            _elapsed = 0f;
            return true;
        }

        public void Cancel()
        {
            if (!IsActive) return;
            _elapsed = Mathf.Max(_elapsed,
                _request.PreparationSeconds + _request.StrokeSeconds + _request.HoldSeconds);
        }

        public void Tick(float dt, float masterWeight)
        {
            _guard.RestoreStaleWrites();
            if (!IsActive) return;
            _elapsed += Mathf.Max(0f, dt);
            float p = _request.PreparationSeconds;
            float s = p + _request.StrokeSeconds;
            float h = s + _request.HoldSeconds;
            float end = h + _request.RetractionSeconds;
            float envelope = _elapsed < p ? Smooth(_elapsed / p) * 0.72f
                : _elapsed < s ? Mathf.Lerp(0.72f, 1f, Smooth((_elapsed - p) / _request.StrokeSeconds))
                : _elapsed < h ? 1f
                : 1f - Smooth((_elapsed - h) / _request.RetractionSeconds);
            float weight = envelope * _request.Intensity * Mathf.Clamp(AmplitudeScale, 0.25f, 1.5f) * Mathf.Clamp01(masterWeight);
            ResolvePose(_request.Kind, out Vector2 upper, out float elbow, out Vector2 wrist, out float curl);
            bool left = _request.Handedness is CoSpeechHandedness.Left or CoSpeechHandedness.Bilateral;
            bool right = _request.Handedness is CoSpeechHandedness.Right or CoSpeechHandedness.Bilateral or CoSpeechHandedness.Automatic;
            if (left) ApplyArm(_lu, _ll, _lh, _leftFingers, new Vector2(upper.x, -upper.y), elbow, wrist, curl, weight);
            if (right) ApplyArm(_ru, _rl, _rh, _rightFingers, upper, elbow, wrist, curl, weight);
            if (_elapsed >= end) { _request = CoSpeechGestureRequest.None; _elapsed = 0f; }
        }

        public void Reset()
        {
            _guard.RestoreStaleWrites();
            _lu = _ll = _lh = _ru = _rl = _rh = null;
            _request = CoSpeechGestureRequest.None;
            _elapsed = 0f;
        }

        private void ApplyArm(Transform upperBone, Transform lower, Transform hand, Transform[] fingers,
            Vector2 upper, float elbow, Vector2 wrist, float curl, float weight)
        {
            if (_request.HasWorldTarget && _request.Kind == GestureCueKind.IndicateObject)
            {
                Vector3 toTarget = _request.WorldTarget - upperBone.position;
                float upperLength = Vector3.Distance(upperBone.position, lower.position);
                float lowerLength = Vector3.Distance(lower.position, hand.position);
                float maxReach = Mathf.Max(0.01f, (upperLength + lowerLength) * 0.96f);
                float minReach = Mathf.Max(0.01f, Mathf.Abs(upperLength - lowerLength) + 0.02f);
                float distance = Mathf.Clamp(toTarget.magnitude, minReach, maxReach);
                if (toTarget.sqrMagnitude > 1e-6f)
                {
                    Transform reference = upperBone.parent != null ? upperBone.parent : upperBone;
                    Vector3 local = reference.InverseTransformDirection(toTarget.normalized);
                    upper.y = Mathf.Clamp(Mathf.Atan2(local.x, Mathf.Max(0.1f, local.z)) * Mathf.Rad2Deg, -38f, 38f);
                    upper.x = Mathf.Clamp(-Mathf.Atan2(local.z, Mathf.Max(0.1f, local.y)) * Mathf.Rad2Deg, -52f, 20f);
                    float cosine = Mathf.Clamp(
                        (upperLength * upperLength + lowerLength * lowerLength - distance * distance) /
                        Mathf.Max(1e-5f, 2f * upperLength * lowerLength), -1f, 1f);
                    elbow = Mathf.Acos(cosine) * Mathf.Rad2Deg - 180f;
                    wrist = new Vector2(4f, Mathf.Clamp(-upper.y * 0.2f, -8f, 8f));
                }
            }
            Apply(upperBone, upper.x * weight, upper.y * weight);
            Apply(lower, elbow * weight, 0f);
            Apply(hand, wrist.x * weight, wrist.y * weight);
            for (int i = 0; i < fingers.Length; i++)
                Apply(fingers[i], (_request.Kind == GestureCueKind.IndicateObject && i == 0 ? 0f : curl) * weight, 0f);
        }

        private void Apply(Transform bone, float sagittal, float lateral)
        {
            if (bone == null || Mathf.Abs(sagittal) + Mathf.Abs(lateral) < 1e-4f) return;
            Quaternion before = bone.localRotation;
            Quaternion delta = ProceduralPoseMath.SwingDelta(bone.parent != null ? bone.parent : bone, sagittal, lateral);
            if (delta == Quaternion.identity) return;
            ProceduralPoseMath.ApplyWorldSwing(bone, delta);
            _guard.Record(bone, before);
        }

        private static void ResolvePose(GestureCueKind kind, out Vector2 upper, out float elbow, out Vector2 wrist, out float curl)
        {
            upper = new Vector2(-11f, 5f); elbow = -18f; wrist = new Vector2(3f, 0f); curl = 5f;
            switch (kind)
            {
                case GestureCueKind.HandToChest: upper = new Vector2(-27f, -14f); elbow = -52f; wrist = new Vector2(-8f, 6f); curl = 9f; break;
                case GestureCueKind.PalmToPlayer: upper = new Vector2(-21f, 12f); elbow = -30f; wrist = new Vector2(11f, 5f); curl = -4f; break;
                case GestureCueKind.IndicateObject: upper = new Vector2(-18f, 8f); elbow = -20f; wrist = new Vector2(5f, 0f); curl = 14f; break;
                case GestureCueKind.Enumerate: upper = new Vector2(-17f, 8f); elbow = -28f; wrist = new Vector2(8f, 3f); curl = 10f; break;
                case GestureCueKind.Uncertain: upper = new Vector2(-9f, 14f); elbow = -22f; wrist = new Vector2(12f, 8f); curl = -3f; break;
                case GestureCueKind.Negative: upper = new Vector2(-8f, 10f); elbow = -17f; wrist = new Vector2(-7f, 9f); break;
                case GestureCueKind.Greeting: upper = new Vector2(-22f, 16f); elbow = -38f; wrist = new Vector2(12f, 10f); curl = -2f; break;
                case GestureCueKind.Emphatic: upper = new Vector2(-15f, 7f); elbow = -24f; wrist = new Vector2(-4f, 0f); curl = 8f; break;
            }
        }

        private static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * t * (t * (t * 6f - 15f) + 10f); }

        private static void BindFingers(Animator a, bool left, Transform[] f)
        {
            f[0] = a.GetBoneTransform(left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal);
            f[1] = a.GetBoneTransform(left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
            f[2] = a.GetBoneTransform(left ? HumanBodyBones.LeftRingProximal : HumanBodyBones.RightRingProximal);
            f[3] = a.GetBoneTransform(left ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal);
        }
    }
}
