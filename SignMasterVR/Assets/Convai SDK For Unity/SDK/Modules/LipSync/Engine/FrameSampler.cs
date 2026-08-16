using System;

namespace Convai.Modules.LipSync
{
    /// <summary>
    ///     Stateless interpolation utilities for blendshape frame data.
    ///     Catmull-Rom (C1 smooth) interpolation with optional temporal smoothing as an opt-in post-process.
    ///     Pure C# -- no UnityEngine dependency.
    /// </summary>
    internal static class FrameSampler
    {
        /// <summary>
        ///     Evaluates a Catmull-Rom spline across all channels with zero allocations.
        ///     Standard tau=0.5 Catmull-Rom provides C1 velocity continuity.
        ///     Output is clamped to [0,1] because the spline can overshoot.
        /// </summary>
        public static void EvaluateCatmullRom(
            float[] p0, float[] p1, float[] p2, float[] p3,
            float alpha, float[] output, int channelCount)
        {
            float t = alpha;
            float t2 = t * t;
            float t3 = t2 * t;

            int limit = MinLength(channelCount, output.Length, p0.Length, p1.Length, p2.Length, p3.Length);

            for (int i = 0; i < limit; i++)
            {
                float v0 = p0[i], v1 = p1[i], v2 = p2[i], v3 = p3[i];
                float result = 0.5f * (
                    (2f * v1) +
                    ((-v0 + v2) * t) +
                    (((2f * v0) - (5f * v1) + (4f * v2) - v3) * t2) +
                    ((-v0 + (3f * v1) - (3f * v2) + v3) * t3));

                output[i] = Math.Clamp(result, 0f, 1f);
            }
        }

        /// <summary>
        ///     Optional temporal smoothing post-process. Disabled when smoothingFactor is 0 (default).
        ///     Uses frame-rate-independent exponential decay. Reads from target and writes into current.
        /// </summary>
        public static void ApplyTemporalSmoothing(
            float[] target, float[] current, float smoothingFactor, float deltaTime, int channelCount)
        {
            if (smoothingFactor <= 0f) return;

            float dt = Math.Clamp(deltaTime, LipSyncConstants.MinDeltaTime, LipSyncConstants.MaxDeltaTimeForSmoothing);
            float scaledDelta = dt * LipSyncConstants.SmoothingReferenceFps;
            float decay = (float)Math.Pow(smoothingFactor, scaledDelta);
            float blend = Math.Clamp(1f - decay, 0f, 1f);

            int limit = Math.Min(channelCount, Math.Min(target.Length, current.Length));
            for (int i = 0; i < limit; i++) current[i] += (target[i] - current[i]) * blend;
        }

        /// <summary>
        /// Approximate low-frequency group delay introduced by the one-pole temporal smoother.
        /// Send-ahead playback can sample this far into its buffered visual timeline so smoothing
        /// does not add a fixed mouth-behind-audio phase error.
        /// </summary>
        public static double GetTemporalSmoothingCompensationSeconds(float smoothingFactor)
        {
            double decay = Math.Clamp(smoothingFactor, 0f, 0.95f);
            if (decay <= 0d) return 0d;

            double delay = decay / (1d - decay) / LipSyncConstants.SmoothingReferenceFps;
            return Math.Min(0.033d, delay);
        }

        private static int MinLength(int a, int b, int c, int d, int e, int f)
        {
            int min = a;
            if (b < min) min = b;
            if (c < min) min = c;
            if (d < min) min = d;
            if (e < min) min = e;
            if (f < min) min = f;
            return min;
        }
    }
}
