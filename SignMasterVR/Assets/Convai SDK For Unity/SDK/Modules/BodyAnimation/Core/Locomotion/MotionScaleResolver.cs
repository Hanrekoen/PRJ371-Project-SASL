using System.Collections.Generic;
using Convai.Modules.BodyAnimation.Data;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Core.Locomotion
{
    /// <summary>
    ///     The pure arithmetic behind rig scale calibration, shared by the runtime
    ///     (<c>ConvaiBodyAnimationController.ResolveMotionScale</c>) and the editor-time
    ///     preflight (<c>BodyAnimationTroubleshooter</c>/<c>BodyAnimationSetupService</c>) so
    ///     both surfaces agree on the same number without duplicating the formula.
    /// </summary>
    internal static class MotionScaleResolver
    {
        /// <summary>Assumed authored scale when a clip's metadata predates clip motion analysis (schema &lt; v3, unanalyzed).</summary>
        internal const float DefaultAuthoredMotionScale = 1f;

        /// <summary>A 1–2% difference is measurement noise, not calibration — snap to exactly 1.</summary>
        internal const float Deadband = 0.02f;

        internal const float MinScale = 0.5f;
        internal const float MaxScale = 2f;

        /// <summary>Clips whose authored scale disagrees with the walk clip by more than this are named in a warning.</summary>
        internal const float ClipMismatchThreshold = 0.05f;

        /// <summary>Average of the three lossyScale components — the uniform component of a (possibly non-uniformly) scaled transform.</summary>
        internal static float UniformScaleOf(Vector3 lossyScale) =>
            (lossyScale.x + lossyScale.y + lossyScale.z) / 3f;

        /// <summary>
        ///     Resolves the single factor every clip-measured distance/speed is multiplied by.
        ///     <paramref name="authoredWalkMotionScale" /> 0 (unknown) assumes the reference rig
        ///     (<see cref="DefaultAuthoredMotionScale" />) so a shipped set whose metadata
        ///     predates this field still calibrates for an unusually sized character.
        /// </summary>
        internal static float Resolve(float humanScale, Vector3 lossyScale, float authoredWalkMotionScale)
        {
            float targetScale = humanScale * UniformScaleOf(lossyScale);
            float authoredScale = authoredWalkMotionScale > 0f ? authoredWalkMotionScale : DefaultAuthoredMotionScale;
            if (authoredScale <= 0f) return 1f;

            float scale = Mathf.Clamp(targetScale / authoredScale, MinScale, MaxScale);
            return Mathf.Abs(scale - 1f) < Deadband ? 1f : scale;
        }

        /// <summary>
        ///     Set-consistency check: names, in one comma-joined string, every clip whose
        ///     analyzed motion scale disagrees with <paramref name="walkAuthoredScale" /> by more
        ///     than <see cref="ClipMismatchThreshold" />. Clips with no recorded scale (unanalyzed
        ///     or metadata written before clip motion was measured) are never counted as disagreeing. Returns null when nothing
        ///     disagrees (including when <paramref name="walkAuthoredScale" /> itself is unknown —
        ///     there is nothing to compare against).
        /// </summary>
        internal static string FindMismatchedClips(
            IReadOnlyList<(string slot, LocomotionClip clip)> clips, float walkAuthoredScale)
        {
            if (clips == null || walkAuthoredScale <= 0f) return null;

            string outliers = null;
            for (int i = 0; i < clips.Count; i++)
            {
                ClipMotionMetadata meta = clips[i].clip?.Metadata;
                if (meta == null || !meta.HasAuthoredMotionScale) continue;

                float ratio = meta.AuthoredMotionScale / walkAuthoredScale;
                if (Mathf.Abs(ratio - 1f) <= ClipMismatchThreshold) continue;

                outliers = outliers == null ? clips[i].slot : $"{outliers}, {clips[i].slot}";
            }

            return outliers;
        }
    }
}
