using Convai.Runtime.Animation.ProceduralPose;
using UnityEngine;

namespace Convai.Modules.Gaze.Core.Solvers
{
    /// <summary>
    ///     A world-space orthonormal gaze frame. It is used only by explicitly calibrated
    ///     custom rigs; uncalibrated rigs retain the Transform-based math paths verbatim.
    /// </summary>
    internal readonly struct GazeReferenceFrame
    {
        public readonly Vector3 Forward;
        public readonly Vector3 Up;
        public readonly Vector3 Right;

        public GazeReferenceFrame(Vector3 forward, Vector3 up)
        {
            Forward = forward.normalized;
            Vector3 projectedUp = Vector3.ProjectOnPlane(up, Forward);
            if (projectedUp.sqrMagnitude < 1e-8f)
                projectedUp = Vector3.up - Vector3.Dot(Vector3.up, Forward) * Forward;
            if (projectedUp.sqrMagnitude < 1e-8f)
                projectedUp = Vector3.right - Vector3.Dot(Vector3.right, Forward) * Forward;

            Up = projectedUp.normalized;
            Right = Vector3.Cross(Up, Forward).normalized;
            Up = Vector3.Cross(Forward, Right).normalized;
        }

        public bool IsValid => Forward.sqrMagnitude > 1e-6f && Up.sqrMagnitude > 1e-6f && Right.sqrMagnitude > 1e-6f;
    }

    /// <summary>
    ///     Shared angle math for the gaze solver chain. All yaw/pitch values are signed
    ///     degrees in the character-root reference frame: yaw positive to the character's
    ///     right, pitch positive upward. Rotations are applied around the reference frame's
    ///     world-space axes (pre-multiplied), which keeps the solve invariant under any
    ///     bind-pose roll authored into individual bones (common on CC4/iClone rigs).
    /// </summary>
    internal static class GazeSolverMath
    {
        /// <summary>
        ///     Computes the signed yaw/pitch (degrees) of the direction from
        ///     <paramref name="fromPosition" /> to <paramref name="targetPoint" /> relative
        ///     to the reference frame's forward axis. Returns <c>false</c> for degenerate
        ///     directions.
        /// </summary>
        public static bool TryGetYawPitch(
            Transform reference,
            Vector3 fromPosition,
            Vector3 targetPoint,
            out float yaw,
            out float pitch)
        {
            yaw = 0f;
            pitch = 0f;
            if (reference == null) return false;

            Vector3 direction = targetPoint - fromPosition;
            if (direction.sqrMagnitude < 1e-8f) return false;

            Vector3 local = reference.InverseTransformDirection(direction.normalized);
            if (local.sqrMagnitude < 1e-8f) return false;

            yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            pitch = Mathf.Asin(Mathf.Clamp(local.y, -1f, 1f)) * Mathf.Rad2Deg;
            return true;
        }

        /// <summary>
        ///     Signed yaw/pitch (degrees) of a world direction expressed in the reference
        ///     frame. Returns <c>false</c> for degenerate directions.
        /// </summary>
        public static bool TryGetDirectionYawPitch(
            Transform reference,
            Vector3 worldDirection,
            out float yaw,
            out float pitch)
        {
            yaw = 0f;
            pitch = 0f;
            if (reference == null || worldDirection.sqrMagnitude < 1e-8f) return false;

            Vector3 local = reference.InverseTransformDirection(worldDirection.normalized);
            if (local.sqrMagnitude < 1e-8f) return false;

            yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            pitch = Mathf.Asin(Mathf.Clamp(local.y, -1f, 1f)) * Mathf.Rad2Deg;
            return true;
        }

        public static bool TryGetYawPitch(
            in GazeReferenceFrame reference,
            Vector3 fromPosition,
            Vector3 targetPoint,
            out float yaw,
            out float pitch)
        {
            yaw = 0f;
            pitch = 0f;
            Vector3 direction = targetPoint - fromPosition;
            if (!reference.IsValid || direction.sqrMagnitude < 1e-8f) return false;
            return TryGetDirectionYawPitch(reference, direction, out yaw, out pitch);
        }

        public static bool TryGetDirectionYawPitch(
            in GazeReferenceFrame reference,
            Vector3 worldDirection,
            out float yaw,
            out float pitch)
        {
            yaw = 0f;
            pitch = 0f;
            if (!reference.IsValid || worldDirection.sqrMagnitude < 1e-8f) return false;
            Vector3 direction = worldDirection.normalized;
            float x = Vector3.Dot(direction, reference.Right);
            float y = Vector3.Dot(direction, reference.Up);
            float z = Vector3.Dot(direction, reference.Forward);
            yaw = Mathf.Atan2(x, z) * Mathf.Rad2Deg;
            pitch = Mathf.Asin(Mathf.Clamp(y, -1f, 1f)) * Mathf.Rad2Deg;
            return true;
        }

        /// <summary>World direction for the given reference-frame yaw/pitch (degrees).</summary>
        public static Vector3 DirectionFromYawPitch(Transform reference, float yaw, float pitch)
        {
            float yawRad = yaw * Mathf.Deg2Rad;
            float pitchRad = pitch * Mathf.Deg2Rad;
            float cosPitch = Mathf.Cos(pitchRad);
            var local = new Vector3(
                Mathf.Sin(yawRad) * cosPitch,
                Mathf.Sin(pitchRad),
                Mathf.Cos(yawRad) * cosPitch);
            return reference != null ? reference.TransformDirection(local) : local;
        }

        public static Vector3 DirectionFromYawPitch(in GazeReferenceFrame reference, float yaw, float pitch)
        {
            float yawRad = yaw * Mathf.Deg2Rad;
            float pitchRad = pitch * Mathf.Deg2Rad;
            float cosPitch = Mathf.Cos(pitchRad);
            return (reference.Right * (Mathf.Sin(yawRad) * cosPitch) +
                    reference.Up * Mathf.Sin(pitchRad) +
                    reference.Forward * (Mathf.Cos(yawRad) * cosPitch)).normalized;
        }

        /// <summary>
        ///     Builds the world-axis rotation that aims by <paramref name="yaw" /> then
        ///     <paramref name="pitch" /> in this reference frame — the exact inverse of
        ///     <see cref="DirectionFromYawPitch(Transform, float, float)" />, so a round trip
        ///     through the two is an identity.
        /// </summary>
        /// <remarks>
        ///     Delegates to <see cref="ProceduralPoseMath.AimSwing" />, the SDK's single
        ///     aim-delta construction, rather than repeating the composition here: the
        ///     operand order is subtle enough that a second copy is a second chance to get it
        ///     wrong, and this file's copy was wrong for exactly that reason (it composed
        ///     <c>Y²·X·Y⁻¹</c> instead of <c>Y·X</c>, mis-aiming by up to 14.8° at the clamp
        ///     corner). See that method's remarks for the derivation.
        /// </remarks>
        public static Quaternion AimSwing(Transform reference, float yaw, float pitch) =>
            reference == null
                ? Quaternion.identity
                : ProceduralPoseMath.AimSwing(reference.right, reference.up, yaw, pitch);

        public static Quaternion AimSwing(in GazeReferenceFrame reference, float yaw, float pitch) =>
            !reference.IsValid
                ? Quaternion.identity
                : ProceduralPoseMath.AimSwing(reference.Right, reference.Up, yaw, pitch);

        /// <summary>
        ///     Builds an additional roll (degrees) about <paramref name="aimedForward" /> — the
        ///     forward axis the bone points along *after* its aim swing, not the reference
        ///     frame's neutral forward.
        /// </summary>
        /// <remarks>
        ///     Rolling about the neutral frame forward is only a head tilt while the head faces
        ///     straight ahead; once the head is yawed, that axis is no longer the head's own
        ///     long axis and the "tilt" comes out as a mix of tilt and yaw. A tilt gesture must
        ///     rotate about where the head is actually looking.
        /// </remarks>
        public static Quaternion RollSwing(Vector3 aimedForward, float rollDegrees)
        {
            if (Mathf.Abs(rollDegrees) < 1e-4f || aimedForward.sqrMagnitude < 1e-8f)
                return Quaternion.identity;

            return Quaternion.AngleAxis(rollDegrees, aimedForward.normalized);
        }

        /// <summary>
        ///     Splits an aim swing across a two-bone chain whose second bone descends from (and
        ///     therefore inherits) the first. See
        ///     <see cref="ProceduralPoseMath.SplitAimSwing" /> for why this cannot be done by
        ///     scaling the yaw/pitch pair twice.
        /// </summary>
        public static void SplitAimSwing(
            Quaternion swing, float firstShare, out Quaternion first, out Quaternion second) =>
            ProceduralPoseMath.SplitAimSwing(swing, firstShare, out first, out second);

        /// <summary>
        ///     Layers <paramref name="worldDelta" /> on top of whatever pose the bone currently
        ///     holds (i.e. the Animator's output this frame).
        /// </summary>
        public static void ApplyDelta(Transform bone, Quaternion worldDelta)
        {
            if (bone == null || worldDelta == Quaternion.identity) return;
            bone.rotation = worldDelta * bone.rotation;
        }

        /// <summary>
        ///     0→1 recruitment ease: 0 below <paramref name="start" />, 1 above
        ///     <paramref name="end" />, smoothstep in between.
        /// </summary>
        public static float RecruitmentEase(float amplitude, float start, float end)
        {
            if (end <= start) return amplitude >= end ? 1f : 0f;
            float t = Mathf.Clamp01((amplitude - start) / (end - start));
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        ///     Soft-limit compression: linear inside <paramref name="softFraction" /> of the
        ///     limit, smoothly compressed to the hard limit beyond it. Sign-preserving.
        /// </summary>
        public static float SoftClamp(float value, float limit, float softFraction)
        {
            if (limit <= 0f) return 0f;

            float magnitude = Mathf.Abs(value);
            float knee = limit * Mathf.Clamp01(softFraction);
            if (magnitude <= knee) return value;

            float overflow = magnitude - knee;
            float room = limit - knee;
            float compressed = room > 1e-4f ? room * (1f - Mathf.Exp(-overflow / room)) : 0f;
            return Mathf.Sign(value) * (knee + compressed);
        }
    }
}
