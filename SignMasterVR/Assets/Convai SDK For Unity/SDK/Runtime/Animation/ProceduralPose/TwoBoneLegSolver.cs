using UnityEngine;

namespace Convai.Runtime.Animation.ProceduralPose
{
    /// <summary>
    ///     Analytic two-bone IK that re-pins a foot after a small pelvis displacement (leg
    ///     compensation), preserving the leg's existing knee-bend plane instead of
    ///     requiring a pole vector. Domain assumption: the pelvis has moved by at most a few
    ///     centimeters (≤ ~4 cm) — the analytic solve below is only guaranteed stable/anatomical
    ///     within that small-displacement regime; it is not a general-purpose IK solver.
    /// </summary>
    /// <remarks>
    ///     The caller (<see cref="ProceduralPoseCompositor" />) guarantees the pelvis has already
    ///     been written this frame before <see cref="Solve" /> runs, so the upper/lower/foot
    ///     transforms read here already reflect the post-pelvis-write pose the solve corrects.
    /// </remarks>
    internal static class TwoBoneLegSolver
    {
        /// <summary>
        ///     Re-aims <paramref name="upperLeg" />/<paramref name="lowerLeg" />/<paramref name="foot" />
        ///     so the foot lands back on <paramref name="targetFootPosition" />/
        ///     <paramref name="targetFootRotation" /> (the pre-pelvis-move foot pose), preserving
        ///     the current knee-bend side. Writes nothing (bails silently, returns
        ///     <see langword="false" />) when the leg is degenerate (near-zero bone length), the
        ///     bend plane cannot be determined, the chain is near full extension (see below),
        ///     or the correction is too small to be worth a solver pass.
        /// </summary>
        /// <returns>
        ///     <see langword="true" /> when the solve wrote all three bones; <see langword="false" />
        ///     when it bailed and left every bone untouched.
        /// </returns>
        public static bool Solve(
            Transform upperLeg,
            Transform lowerLeg,
            Transform foot,
            Vector3 targetFootPosition,
            Quaternion targetFootRotation,
            AnimatedAdditivePoseGuard guard)
        {
            if (upperLeg == null || lowerLeg == null || foot == null) return false;

            // Step 1: bone lengths. A near-zero segment means the leg rig is degenerate (or not
            // actually a leg) — bail rather than produce a NaN/garbage rotation.
            float a = Vector3.Distance(lowerLeg.position, upperLeg.position);
            float b = Vector3.Distance(foot.position, lowerLeg.position);
            if (a < 1e-4f || b < 1e-4f) return false;

            // Step 1b (extension gate): a chain at (or past) ~full
            // extension has a numerically meaningless bend plane — the knee-bend-side
            // preservation below cannot mean anything on a straight leg, so solving it is
            // exactly the degenerate straight-leg regime this gate exists to avoid. Checked
            // against the CURRENT (pre-solve) foot position/total segment length, ahead of the
            // reach clamp and bend-plane recovery below, so a fallback bend plane is never even
            // attempted on a chain this straight. See also
            // <see cref="ProceduralPoseCompositor.LegChainNearFullExtension" />'s slightly
            // tighter 0.99 threshold, which the compositor uses to decide whether to feed this
            // solver at all — this solver's own 0.995 is a second, independent safety line.
            float currentExtension = Vector3.Distance(foot.position, upperLeg.position) / (a + b);
            if (currentExtension > 0.995f) return false;

            // Step 1c (displacement gate): a sub-5mm AND sub-half-degree
            // correction is invisible on screen — not worth a solver pass (three guarded
            // rotation writes). Either error alone clearing its threshold still proceeds.
            float displacementSqrMeters = (targetFootPosition - foot.position).sqrMagnitude;
            float rotationErrorDegrees = Quaternion.Angle(foot.rotation, targetFootRotation);
            if (displacementSqrMeters < 0.005f * 0.005f && rotationErrorDegrees < 0.5f) return false;

            // Step 2: clamp the reach so the law-of-cosines triangle below always stays valid
            // (strictly inside the [|a-b|, a+b] range).
            Vector3 toTarget = targetFootPosition - upperLeg.position;
            float d = Mathf.Clamp(toTarget.magnitude, 1e-4f, a + b - 1e-4f);

            // Step 3: bend-plane normal from the CURRENT pose (preserves whichever way the knee
            // already bends). Degenerate (near-straight) legs fall back to a normal built from
            // the reach direction and the thigh's parent's right axis; if that is also
            // degenerate, there is no reliable bend plane to preserve — bail.
            Vector3 currentThighToKnee = lowerLeg.position - upperLeg.position;
            Vector3 currentThighToFootStraight = foot.position - upperLeg.position;
            Vector3 n = Vector3.Cross(currentThighToKnee, currentThighToFootStraight);
            if (n.sqrMagnitude < 1e-8f)
            {
                Vector3 fallbackRight = upperLeg.parent != null ? upperLeg.parent.right : Vector3.right;
                n = Vector3.Cross(toTarget, fallbackRight);
                if (n.sqrMagnitude < 1e-8f) return false;
            }
            n.Normalize();

            // Step 4: law of cosines for the thigh-hip angle, then rotate the reach direction by
            // that angle around the bend-plane normal — twice, once each way — and keep whichever
            // candidate keeps the knee on the same side as it started (no pole vector needed).
            float cosThigh = Mathf.Clamp((a * a + d * d - b * b) / (2f * a * d), -1f, 1f);
            float thighAngleDegrees = Mathf.Acos(cosThigh) * Mathf.Rad2Deg;

            Vector3 toTargetDirection = toTarget.normalized;
            Vector3 candidatePositive = Quaternion.AngleAxis(thighAngleDegrees, n) * toTargetDirection;
            Vector3 candidateNegative = Quaternion.AngleAxis(-thighAngleDegrees, n) * toTargetDirection;

            Vector3 currentKneeDirection = currentThighToKnee.normalized;
            Vector3 desiredThighToKnee =
                Vector3.Dot(candidatePositive, currentKneeDirection) >=
                Vector3.Dot(candidateNegative, currentKneeDirection)
                    ? candidatePositive
                    : candidateNegative;

            // Step 5: guarded writes, upper first. Rotating upperLeg's WORLD rotation rigidly
            // carries lowerLeg/foot's world positions with it (Unity recomputes child world
            // transforms from local offsets immediately), so lowerLeg.position below already
            // reflects the post-rotation knee position — no manual recomputation needed.
            Quaternion upperPreWrite = upperLeg.localRotation;
            Quaternion upperDelta = Quaternion.FromToRotation(currentKneeDirection, desiredThighToKnee);
            upperLeg.rotation = upperDelta * upperLeg.rotation;
            guard?.Record(upperLeg, upperPreWrite);

            Vector3 newKneeToFoot = foot.position - lowerLeg.position;

            Quaternion lowerPreWrite = lowerLeg.localRotation;
            Quaternion lowerDelta = Quaternion.FromToRotation(newKneeToFoot, targetFootPosition - lowerLeg.position);
            lowerLeg.rotation = lowerDelta * lowerLeg.rotation;
            guard?.Record(lowerLeg, lowerPreWrite);

            Quaternion footPreWrite = foot.localRotation;
            foot.rotation = targetFootRotation;
            guard?.Record(foot, footPreWrite);

            return true;
        }
    }
}
