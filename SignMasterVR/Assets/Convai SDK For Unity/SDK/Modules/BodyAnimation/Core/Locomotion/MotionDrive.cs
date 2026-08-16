using Convai.Modules.BodyAnimation.Data;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Core.Locomotion
{
    /// <summary>
    ///     Curve evaluation for scripted root drive: the ground speed and yaw the character
    ///     should have at a given normalized time of an in-place one-shot, derived from the
    ///     clip's analyzed distance/yaw curves. The state machine feeds the speed to the
    ///     NavMeshAgent (position) and applies the yaw to the transform (rotation).
    /// </summary>
    internal static class MotionDrive
    {
        private const float SampleWindow = 0.02f;
        private const float NominalTurnAnticipation = 0.08f;

        /// <summary>
        ///     One-shots crossfade out at the handoff point; finish fallback turn drive just
        ///     before that so the residual settle never has to absorb the main rotation.
        /// </summary>
        private const float NominalTurnMinDriveWindow = 0.1f;

        /// <summary>
        ///     Ground speed (m/s) at <paramref name="normalizedTime" /> — the numerical
        ///     derivative of the authored distance curve, scaled by <paramref name="motionScale" />
        ///     (the ratio between this character's rig and the sample rig the clip was
        ///     analyzed on; 1 = no correction). 0 without distance data.
        /// </summary>
        public static float SpeedAt(
            ClipMotionMetadata meta, float normalizedTime, float clipLength, float motionScale = 1f)
        {
            if (meta == null || !meta.HasDistance || clipLength <= 0f) return 0f;

            float t0 = Mathf.Clamp01(normalizedTime - SampleWindow);
            float t1 = Mathf.Clamp01(normalizedTime + SampleWindow);
            if (t1 - t0 < 1e-4f) return 0f;

            float meters = meta.EvaluateDistance(t1) - meta.EvaluateDistance(t0);
            float seconds = (t1 - t0) * clipLength;
            return Mathf.Max(0f, meters / seconds) * motionScale;
        }

        /// <summary>
        ///     Yaw delta (deg) authored between two normalized times, scaled by
        ///     <paramref name="yawScale" /> (actual required yaw / authored yaw).
        /// </summary>
        public static float YawDelta(
            ClipMotionMetadata meta, float previousNormalizedTime, float normalizedTime, float yawScale)
        {
            if (meta == null || !meta.HasYaw) return 0f;
            return (meta.EvaluateYaw(normalizedTime) - meta.EvaluateYaw(previousNormalizedTime)) * yawScale;
        }

        /// <summary>
        ///     Scale factor mapping a clip's authored yaw onto the actually required yaw,
        ///     clamped so a badly matched clip never over/under-rotates grotesquely.
        /// </summary>
        public static float YawScale(float requiredYaw, float authoredYaw, float min = 0.6f, float max = 1.4f)
        {
            if (Mathf.Abs(authoredYaw) < 1f) return 0f;

            float ratio = requiredYaw / authoredYaw;
            // Opposite signs: the clip turns the wrong way for this request — scripted
            // yaw would rotate AWAY from the target. No scale; the caller falls back.
            if (ratio <= 0f) return 0f;

            return Mathf.Clamp(ratio, min, max);
        }

        /// <summary>
        ///     Signed yaw for a turn-in-place clip whose analyzer yaw is missing or untrusted,
        ///     driven from the clip slot's nominal 90/180 degree authoring intent.
        /// </summary>
        public static float NominalTurnYaw(
            float authoredYaw,
            float normalizedTime,
            float driveEndNormalizedTime)
        {
            return authoredYaw * NominalTurnProgress(normalizedTime, driveEndNormalizedTime);
        }

        /// <summary>
        ///     Delta counterpart of <see cref="NominalTurnYaw" /> for scripted root rotation.
        ///     The smootherstep profile gives the turn a planted anticipation, acceleration,
        ///     deceleration, and no angular snap at the handoff.
        /// </summary>
        public static float NominalTurnYawDelta(
            float authoredYaw,
            float previousNormalizedTime,
            float normalizedTime,
            float yawScale,
            float driveEndNormalizedTime)
        {
            float previousYaw = NominalTurnYaw(authoredYaw, previousNormalizedTime, driveEndNormalizedTime);
            float currentYaw = NominalTurnYaw(authoredYaw, normalizedTime, driveEndNormalizedTime);
            return (currentYaw - previousYaw) * yawScale;
        }

        private static float NominalTurnProgress(float normalizedTime, float driveEndNormalizedTime)
        {
            float start = NominalTurnAnticipation;
            float end = Mathf.Clamp(driveEndNormalizedTime, start + NominalTurnMinDriveWindow, 1f);
            float t = Mathf.Clamp01((normalizedTime - start) / (end - start));

            // Quintic smootherstep: zero velocity and acceleration at both ends. It reads
            // closer to a weight-shift turn than a linear root swivel when analyzer yaw is absent.
            return t * t * t * (t * (t * 6f - 15f) + 10f);
        }

        /// <summary>
        ///     First normalized time where the clip has covered <paramref name="meters" /> of
        ///     its authored travel — used to trim the stationary wind-up off a start clip.
        ///     0 when the clip has no distance data or never reaches the distance.
        ///     <paramref name="meters" /> is a real/world distance; it is converted into the
        ///     clip's own (unscaled) authored space by dividing by <paramref name="motionScale" />
        ///     before it is compared against the curve, which is still in authored units.
        /// </summary>
        public static float NormalizedTimeAtDistance(ClipMotionMetadata meta, float meters, float motionScale = 1f)
        {
            if (meta == null || !meta.HasDistance || meters <= 0f) return 0f;

            float authoredMeters = motionScale > 0f ? meters / motionScale : meters;
            if (meta.AuthoredDistance <= authoredMeters) return 0f;

            const int steps = 64;
            float previousTime = 0f;
            float previousDistance = 0f;
            for (int i = 1; i <= steps; i++)
            {
                float t = (float)i / steps;
                float d = meta.EvaluateDistance(t);
                if (d >= authoredMeters)
                {
                    float span = d - previousDistance;
                    float k = span > 1e-5f ? (authoredMeters - previousDistance) / span : 1f;
                    return Mathf.Lerp(previousTime, t, k);
                }

                previousTime = t;
                previousDistance = d;
            }

            return 0f;
        }
    }
}
