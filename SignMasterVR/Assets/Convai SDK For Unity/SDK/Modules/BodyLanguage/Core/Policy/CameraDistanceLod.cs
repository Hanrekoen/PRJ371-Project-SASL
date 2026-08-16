using UnityEngine;

namespace Convai.Modules.BodyLanguage.Core.Policy
{
    /// <summary>
    ///     Pure static piecewise-linear mapping from camera distance (meters) to a micro-motion
    ///     amplitude scale (camera-distance amplitude LOD). Far
    ///     cameras read slightly larger sway/hand-micro motion for readability; extreme
    ///     close-ups read slightly subtler for intimacy. Feel-pass tunable constants, kept
    ///     internal (no profile surface beyond the enable toggle — see
    ///     <c>ConvaiBodyLanguageProfile.EnableCameraDistanceLod</c>).
    /// </summary>
    internal static class CameraDistanceLod
    {
        /// <summary>Distance (meters) at and below which the scale floors at <see cref="NearScale" />.</summary>
        internal const float NearDistanceMeters = 1f;

        /// <summary>Amplitude scale at <see cref="NearDistanceMeters" /> and closer — subtler for intimacy.</summary>
        internal const float NearScale = 0.7f;

        /// <summary>Distance (meters) at which the scale reaches <see cref="NeutralScale" /> (1.0) on the way out from near.</summary>
        internal const float NeutralNearDistanceMeters = 2.5f;

        /// <summary>Neutral (no-op) amplitude scale — the plateau between <see cref="NeutralNearDistanceMeters" /> and <see cref="NeutralFarDistanceMeters" />.</summary>
        internal const float NeutralScale = 1.0f;

        /// <summary>Distance (meters) at which the neutral plateau ends and the far ramp begins.</summary>
        internal const float NeutralFarDistanceMeters = 6f;

        /// <summary>Distance (meters) at and beyond which the scale caps at <see cref="FarScale" />.</summary>
        internal const float FarDistanceMeters = 12f;

        /// <summary>Amplitude scale at <see cref="FarDistanceMeters" /> and beyond — larger for readability.</summary>
        internal const float FarScale = 1.3f;

        /// <summary>Maximum rate (scale units/second) the APPLIED scale may slew — see the controller's <c>Mathf.MoveTowards</c> use.</summary>
        internal const float MaxScaleChangePerSecond = 0.5f;

        /// <summary>
        ///     Resolves the raw (unslewed) target amplitude scale for a camera distance (meters):
        ///     <see cref="NearScale" /> at or below <see cref="NearDistanceMeters" />, linear up
        ///     to <see cref="NeutralScale" /> at <see cref="NeutralNearDistanceMeters" />, flat
        ///     through <see cref="NeutralFarDistanceMeters" />, linear up to <see cref="FarScale" />
        ///     at <see cref="FarDistanceMeters" />, clamped beyond. Monotone non-decreasing in
        ///     distance across the whole domain. Negative input is treated as 0.
        /// </summary>
        internal static float ScaleForDistance(float distanceMeters)
        {
            float d = Mathf.Max(0f, distanceMeters);

            if (d <= NearDistanceMeters) return NearScale;
            if (d <= NeutralNearDistanceMeters)
                return Mathf.Lerp(NearScale, NeutralScale,
                    (d - NearDistanceMeters) / (NeutralNearDistanceMeters - NearDistanceMeters));
            if (d <= NeutralFarDistanceMeters) return NeutralScale;
            if (d <= FarDistanceMeters)
                return Mathf.Lerp(NeutralScale, FarScale,
                    (d - NeutralFarDistanceMeters) / (FarDistanceMeters - NeutralFarDistanceMeters));

            return FarScale;
        }
    }
}
