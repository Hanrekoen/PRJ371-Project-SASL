using UnityEngine;

namespace Convai.Modules.BodyAnimation.Core.Locomotion
{
    /// <summary>
    ///     The pure arithmetic behind deriving a NavMeshAgent's capsule from the rig it is
    ///     steering, extracted from <c>ConvaiNavMeshLocomotion.ConfigureAgentDimensionsFromRig</c>
    ///     so it is unit-testable without a live Animator/avatar.
    /// </summary>
    internal static class NavMeshAgentDimensionResolver
    {
        internal const float MinHeight = 1.2f;
        internal const float MaxHeight = 2.4f;
        internal const float RadiusRatio = 0.17f;

        /// <summary>Approximates the crown above the head bone (which sits at eye/brow height, not the top of the skull).</summary>
        internal const float HeadCrownFactor = 1.06f;

        /// <summary>Generic human height estimate used when the head bone is unmapped.</summary>
        internal const float FallbackHumanScaleFactor = 1.7f;

        /// <summary>
        ///     Resolves (height, radius) for the agent capsule. <paramref name="headHeightAboveRoot" />
        ///     is the head bone's height above the character root in world space, or null when the
        ///     head bone is unmapped (falls back to <paramref name="humanScale" />). Both outputs are
        ///     clamped so an implausible measurement never produces an unusable agent.
        /// </summary>
        internal static (float height, float radius) Resolve(float? headHeightAboveRoot, float humanScale)
        {
            float height = headHeightAboveRoot.HasValue
                ? headHeightAboveRoot.Value * HeadCrownFactor
                : humanScale * FallbackHumanScaleFactor;

            height = Mathf.Clamp(height, MinHeight, MaxHeight);
            float radius = height * RadiusRatio;
            return (height, radius);
        }
    }
}
