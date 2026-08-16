using UnityEngine;

namespace Convai.Runtime.Animation.ProceduralPose
{
    /// <summary>
    ///     Shared math for procedural additive pose layers: critically-damped spring smoothing,
    ///     swing-only delta composition for the spine chain, full pitch/yaw/roll composition
    ///     for head-style gestures, torso-aim composition for the gaze module's late torso
    ///     entry, and the world-space apply step every one of them shares. Mirrors the numerical
    ///     recipes the gaze solver chain uses (module isolation forbids referencing that type
    ///     directly — see house rule); this Runtime-owned type is the single home both
    ///     BodyLanguage's solvers and Gaze's torso branch route through via
    ///     <see cref="ProceduralPoseCompositor" />.
    /// </summary>
    internal static class ProceduralPoseMath
    {
        /// <summary>
        ///     Critically-damped spring toward <paramref name="target" /> with a hard angular
        ///     speed clamp (deg/s). <paramref name="sharpness" /> keeps its "higher = snappier"
        ///     meaning; a bell-shaped velocity profile reads as muscle-driven rather than the
        ///     mechanical instant-peak-velocity of plain exponential smoothing.
        /// </summary>
        public static float SpringValue(
            float current,
            float target,
            ref float velocity,
            float sharpness,
            float maxSpeedPerSecond,
            float deltaTime)
        {
            float smoothTime = 2f / Mathf.Max(0.5f, sharpness);
            return Mathf.SmoothDamp(current, target, ref velocity, smoothTime,
                Mathf.Max(0.01f, maxSpeedPerSecond), deltaTime);
        }

        /// <summary>
        ///     Builds a swing-only rotation delta from a sagittal (pitch, around the
        ///     reference's right axis) and lateral (roll, around the reference's forward axis)
        ///     angle pair, expressed in the reference frame's world axes. Deliberately excludes
        ///     any rotation around the reference's up axis, which would twist the spine's long
        ///     axis — the one rotation posture/breath must never apply.
        /// </summary>
        public static Quaternion SwingDelta(Transform reference, float sagittalDegrees, float lateralDegrees)
        {
            if (reference == null) return Quaternion.identity;
            if (Mathf.Abs(sagittalDegrees) < 1e-5f && Mathf.Abs(lateralDegrees) < 1e-5f)
                return Quaternion.identity;

            Vector3 right = reference.right;
            Vector3 forward = reference.forward;
            return Quaternion.AngleAxis(lateralDegrees, forward) * Quaternion.AngleAxis(-sagittalDegrees, right);
        }

        /// <summary>
        ///     Builds a full three-axis rotation delta (pitch/yaw/roll, all in the reference
        ///     frame's world axes) for callers that need roll — unlike <see cref="SwingDelta" />,
        ///     which deliberately excludes twist for the spine chain. Only the no-consumer
        ///     head-gesture fallback uses this: Neck/Head tilt (roll) is a legitimate head
        ///     motion, whereas spine roll would look like a twisted torso.
        /// </summary>
        public static Quaternion PitchYawRollDelta(Transform reference, float pitchDegrees, float yawDegrees, float rollDegrees)
        {
            if (reference == null) return Quaternion.identity;
            if (Mathf.Abs(pitchDegrees) < 1e-5f && Mathf.Abs(yawDegrees) < 1e-5f && Mathf.Abs(rollDegrees) < 1e-5f)
                return Quaternion.identity;

            Vector3 right = reference.right;
            Vector3 up = reference.up;
            Vector3 forward = reference.forward;
            return Quaternion.AngleAxis(yawDegrees, up) *
                   Quaternion.AngleAxis(-pitchDegrees, right) *
                   Quaternion.AngleAxis(rollDegrees, forward);
        }

        /// <summary>
        ///     Builds the rotation that aims a bone by <paramref name="yawDegrees" /> around
        ///     <paramref name="up" /> and then <paramref name="pitchDegrees" /> around the
        ///     yaw-rotated right axis, expressed in world axes. The single aim-delta
        ///     construction in the SDK: the gaze head/neck chain, the gaze torso entry
        ///     (<see cref="TorsoAimDelta" />) and any future aim rung all route through here so
        ///     there is exactly one place the convention lives.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>Operand order is load-bearing.</b> With <c>Y = AngleAxis(yaw, up)</c> and
        ///         <c>X = AngleAxis(-pitch, right)</c>, the rotation meaning "yaw, then pitch
        ///         about the yawed right axis" is <c>Y·X</c>, which — via the conjugation
        ///         identity <c>AngleAxis(θ, Y·a) = Y·AngleAxis(θ, a)·Y⁻¹</c> — is written here
        ///         as <c>AngleAxis(-pitch, Y·right) · Y</c>, with the yawed-right pitch on the
        ///         LEFT. Putting it on the right instead yields <c>Y²·X·Y⁻¹</c>, which aims
        ///         nowhere near the requested direction once both angles are large (measured:
        ///         3.7° off at yaw 40°/pitch -15°, 14.8° off at yaw 55°/pitch -32°). This is
        ///         the exact inverse of <c>DirectionFromYawPitch</c>-style angle extraction, so
        ///         a round trip through the two is an identity.
        ///     </para>
        /// </remarks>
        public static Quaternion AimSwing(Vector3 right, Vector3 up, float yawDegrees, float pitchDegrees)
        {
            if (Mathf.Abs(yawDegrees) < 1e-4f && Mathf.Abs(pitchDegrees) < 1e-4f) return Quaternion.identity;

            Quaternion yaw = Quaternion.AngleAxis(yawDegrees, up);
            return Quaternion.AngleAxis(-pitchDegrees, yaw * right) * yaw;
        }

        /// <summary>
        ///     Splits an aim rotation into two consecutive parts carrying
        ///     <paramref name="firstShare" /> and the exact remainder, for distributing one aim
        ///     across a two-bone chain (neck/head, chest/upper-chest) where the second bone is a
        ///     descendant of the first and therefore inherits its rotation.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>Why a slerp and not two scaled angle pairs.</b> Building
        ///         <c>AimSwing(yaw·s, pitch·s)</c> and <c>AimSwing(yaw·(1-s), pitch·(1-s))</c>
        ///         and composing them does NOT reconstruct <c>AimSwing(yaw, pitch)</c>: yaw and
        ///         pitch are rotations about different axes and do not commute, so the residual
        ///         shows up as a parasitic ROLL on the descendant bone — a visible head tilt
        ///         whenever yaw and pitch are both non-zero (measured: 6.3° at yaw 40°/pitch
        ///         -15°, 16.6° at the clamp corner). Slerping along the aim's own single axis is
        ///         a pure angle scale, so <c>second * first</c> reproduces
        ///         <paramref name="swing" /> at any share, with no roll and no aim error.
        ///     </para>
        ///     <para>
        ///         The parts are returned in application order: apply <paramref name="first" />
        ///         to the ancestor bone and <paramref name="second" /> to the descendant, which
        ///         has already inherited the ancestor's world rotation.
        ///     </para>
        /// </remarks>
        public static void SplitAimSwing(
            Quaternion swing,
            float firstShare,
            out Quaternion first,
            out Quaternion second)
        {
            first = Quaternion.Slerp(Quaternion.identity, swing, Mathf.Clamp01(firstShare));
            second = swing * Quaternion.Inverse(first);
        }

        /// <summary>
        ///     Aim delta in a <see cref="Transform" />'s frame — the gaze module's torso entry
        ///     (<see cref="ProceduralPoseCompositor.ComposeTorsoAim" />), which routes its write
        ///     through the shared guard's single restore-once-per-frame protocol instead of
        ///     gaze's own guard. Thin frame-resolving wrapper over <see cref="AimSwing" />.
        /// </summary>
        public static Quaternion TorsoAimDelta(Transform reference, float yawDegrees, float pitchDegrees)
        {
            if (reference == null) return Quaternion.identity;
            return AimSwing(reference.right, reference.up, yawDegrees, pitchDegrees);
        }

        /// <summary>
        ///     Applies <paramref name="worldDelta" /> as a world-space pre-multiply on top of
        ///     <paramref name="bone" />'s current (animated) local rotation, matching the
        ///     composition <c>animatedLocalRotation * calibratedDelta</c> the plan specifies:
        ///     the delta is defined in world axes so it survives an animated or static pose
        ///     equally, then folded back into the bone's local space via its parent.
        /// </summary>
        public static void ApplyWorldSwing(Transform bone, Quaternion worldDelta)
        {
            if (bone == null) return;
            bone.rotation = worldDelta * bone.rotation;
        }
    }
}
