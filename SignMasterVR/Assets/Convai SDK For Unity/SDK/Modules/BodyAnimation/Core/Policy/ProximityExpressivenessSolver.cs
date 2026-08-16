using UnityEngine;

namespace Convai.Modules.BodyAnimation.Core.Policy
{
    /// <summary>
    ///     Pure two-point distance-to-multiplier mapping for proximity-scaled expressiveness
    ///    : closer conversation reads subtler, farther conversation reads broader.
    ///     Linear between the near and far distances, clamped to a fixed overall range beyond
    ///     them. A static pure function so it is unit-testable without a layer or graph.
    /// </summary>
    internal static class ProximityExpressivenessSolver
    {
        /// <summary>
        ///     Resolves the target expressiveness multiplier for <paramref name="distance" />.
        ///     At or below <paramref name="nearDistance" /> the result is <paramref name="nearScale" />;
        ///     at or beyond <paramref name="farDistance" /> it is <paramref name="farScale" />; in
        ///     between it is linearly interpolated. The result is always clamped to
        ///     <paramref name="clampMin" />..<paramref name="clampMax" />, regardless of the
        ///     configured near/far scales, so a misconfigured profile cannot push gesture size
        ///     outside the safe range.
        /// </summary>
        public static float ComputeTargetMultiplier(
            float distance,
            float nearDistance,
            float nearScale,
            float farDistance,
            float farScale,
            float clampMin,
            float clampMax)
        {
            float value;
            if (farDistance <= nearDistance)
            {
                // Degenerate configuration (far at or inside near): no meaningful interval to
                // interpolate across — fall back to the far scale so "closer wins" never traps
                // the multiplier at the near value for every distance.
                value = farScale;
            }
            else
            {
                float t = Mathf.InverseLerp(nearDistance, farDistance, distance);
                value = Mathf.Lerp(nearScale, farScale, t);
            }

            return Mathf.Clamp(value, Mathf.Min(clampMin, clampMax), Mathf.Max(clampMin, clampMax));
        }
    }
}
