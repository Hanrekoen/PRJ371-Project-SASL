using Convai.Modules.BodyAnimation.Data;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Core.Locomotion
{
    /// <summary>
    ///     A world-space anchor pose (position + yaw) an anchored action approaches and
    ///     aligns to. Flat (ground-plane) yaw only — every alignment computation in this
    ///     module works on the horizontal plane.
    /// </summary>
    internal readonly struct AnchorPose
    {
        public readonly Vector3 Position;
        public readonly float YawDegrees;

        public AnchorPose(Vector3 position, float yawDegrees)
        {
            Position = position;
            YawDegrees = yawDegrees;
        }
    }

    /// <summary>
    ///     Pure math for the <c>PlayActionAt</c> "sit / pick-up / use-prop" flow: approach
    ///     point computation, target facing, the alignment envelope check, and the
    ///     smoothstep-eased position/yaw lerp. No UnityEngine.Object dependency — safe to
    ///     unit test without a scene.
    /// </summary>
    internal static class AnchorAlignmentSolver
    {
        /// <summary>
        ///     World-space point the character should stand at: the anchor's position offset
        ///     by <paramref name="approachOffsetLocal" /> in the anchor's own local space
        ///     (rotated by its yaw).
        /// </summary>
        public static Vector3 ComputeApproachPoint(in AnchorPose anchor, Vector3 approachOffsetLocal)
        {
            Quaternion rotation = Quaternion.Euler(0f, anchor.YawDegrees, 0f);
            return anchor.Position + rotation * approachOffsetLocal;
        }

        /// <summary>
        ///     The yaw (degrees) the character should end up facing once aligned, per
        ///     <paramref name="facingMode" />. <see cref="ActionFacingMode.None" /> returns
        ///     <paramref name="currentYaw" /> unchanged — callers should treat that mode as
        ///     "no yaw target" and skip the yaw envelope/lerp entirely.
        /// </summary>
        public static float ComputeTargetYaw(
            in AnchorPose anchor, Vector3 targetPosition, ActionFacingMode facingMode, float currentYaw)
        {
            switch (facingMode)
            {
                case ActionFacingMode.AnchorForward:
                    return anchor.YawDegrees;

                case ActionFacingMode.FaceAnchor:
                    Vector3 toAnchor = anchor.Position - targetPosition;
                    toAnchor.y = 0f;
                    if (toAnchor.sqrMagnitude < 1e-6f) return anchor.YawDegrees;
                    return Mathf.Atan2(toAnchor.x, toAnchor.z) * Mathf.Rad2Deg;

                default: // ActionFacingMode.None
                    return currentYaw;
            }
        }

        /// <summary>
        ///     True when <paramref name="currentPosition" />/<paramref name="currentYaw" /> are
        ///     already close enough to <paramref name="targetPosition" />/<paramref name="targetYaw" />
        ///     (ground-plane XZ distance — the Y axis is ignored entirely, since anchor height
        ///     never participates in alignment — and yaw error) to run the alignment lerp.
        ///     Facing mode <see cref="ActionFacingMode.None" /> skips the yaw check entirely.
        /// </summary>
        public static bool IsWithinEnvelope(
            Vector3 currentPosition,
            float currentYaw,
            Vector3 targetPosition,
            float targetYaw,
            ActionFacingMode facingMode,
            float maxDistance,
            float maxYawDegrees)
        {
            Vector3 delta = targetPosition - currentPosition;
            delta.y = 0f;
            if (delta.magnitude > maxDistance) return false;

            if (facingMode == ActionFacingMode.None) return true;

            float yawError = Mathf.Abs(Mathf.DeltaAngle(currentYaw, targetYaw));
            return yawError <= maxYawDegrees;
        }

        /// <summary>Monotonic 0..1 smoothstep ease (zero velocity at both ends).</summary>
        public static float Smoothstep01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        ///     Smoothstep-eased position lerp, XZ (ground-plane) only — <paramref name="from" />'s
        ///     Y always passes through untouched, regardless of <paramref name="to" />'s Y. The
        ///     character's height is never re-derived from the anchor: an anchor authored at
        ///     seat/prop height must not sink or lift the root.
        /// </summary>
        public static Vector3 LerpPosition(Vector3 from, Vector3 to, float t)
        {
            float eased = Smoothstep01(t);
            return new Vector3(
                Mathf.Lerp(from.x, to.x, eased),
                from.y,
                Mathf.Lerp(from.z, to.z, eased));
        }

        /// <summary>Smoothstep-eased yaw lerp (shortest angular path).</summary>
        public static float LerpYaw(float fromDegrees, float toDegrees, float t) =>
            Mathf.LerpAngle(fromDegrees, toDegrees, Smoothstep01(t));
    }
}
