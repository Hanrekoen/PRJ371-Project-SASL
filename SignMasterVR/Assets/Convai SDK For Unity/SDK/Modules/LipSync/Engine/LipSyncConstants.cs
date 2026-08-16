namespace Convai.Modules.LipSync
{
    /// <summary>
    ///     Shared numeric constants for the LipSync engine, sampler, fade, and sink.
    ///     Centralizing these values prevents behavior drift across runtime modules.
    /// </summary>
    internal static class LipSyncConstants
    {
        // Delta-time clamps used by fade and smoothing calculations.

        /// <summary>
        ///     Minimum frame delta used for fade and smoothing calculations.
        ///     Equivalent to 600 FPS; prevents division by near-zero on very fast hardware.
        /// </summary>
        public const float MinDeltaTime = 1f / 600f;

        /// <summary>
        ///     Maximum frame delta used for <see cref="FadeController" /> progress advancement.
        ///     Clamps at 20 FPS to avoid visible jumps on long frames.
        /// </summary>
        public const float MaxDeltaTimeForFade = 1f / 20f;

        /// <summary>
        ///     Maximum frame delta used for <see cref="FrameSampler" /> temporal smoothing.
        ///     Clamps at 30 FPS because exponential decay is sensitive to large delta values.
        /// </summary>
        public const float MaxDeltaTimeForSmoothing = 1f / 30f;

        // Output thresholds and interpolation guards.

        /// <summary>
        ///     Minimum absolute change in a blendshape weight required to call
        ///     <c>SkinnedMeshRenderer.SetBlendShapeWeight</c>. Smaller deltas are skipped
        ///     to reduce Unity API overhead when values are nearly static.
        ///     Expressed as a normalized [0, 1] weight (not Unity's internal 0-100 scale).
        /// </summary>
        public const float BlendshapeWriteEpsilon = 0.05f;

        /// <summary>
        ///     Minimum normalized blendshape output magnitude considered active.
        ///     Used by <see cref="LipSyncPlaybackEngine.HasActiveOutput" />.
        /// </summary>
        public const float ActiveOutputThreshold = 0.0001f;

        /// <summary>
        ///     Minimum time span (seconds) between adjacent frames required for interpolation.
        /// </summary>
        public const float MinFrameSpanSeconds = 0.0001f;

        // Reference frame rate for smoothing normalization.

        /// <summary>
        ///     Reference FPS used to make temporal smoothing frame-rate independent.
        ///     Smoothing factor is defined as the decay per reference frame at this rate.
        /// </summary>
        public const float SmoothingReferenceFps = 60f;
    }
}
