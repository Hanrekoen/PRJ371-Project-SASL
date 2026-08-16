using Convai.Modules.BodyAnimation.Data;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Core.Policy
{
    /// <summary>
    ///     Pure mapping from the two persona sliders (<see cref="ConvaiBodyAnimationConfig.GestureLiveliness" />,
    ///     <see cref="ConvaiBodyAnimationConfig.Calmness" />) to the small set of knobs they scale
    ///    . Every mapping is neutral at the sliders' default value of 1, so a config
    ///     built with the sliders untouched produces byte-identical behavior to before this
    ///     feature existed. Kept in one place, as static pure functions, so tests can pin the
    ///     exact math without spinning up a layer or graph.
    /// </summary>
    internal static class PersonaScalars
    {
        /// <summary>Lower clamp applied to Gesture Liveliness before it divides the beat refractory window.</summary>
        private const float MinLivelinessForRefractory = 0.25f;

        /// <summary>Multiplier applied to talk-gesture weight (folds in alongside the proximity multiplier). Neutral (1) at liveliness = 1.</summary>
        public static float ResolveGestureWeightScale(ConvaiBodyAnimationConfig config) =>
            Mathf.Clamp(config.GestureLiveliness, 0f, 2f);

        /// <summary>
        ///     Probability [0..1] that a talk variant actually switches on loop-wrap. Neutral (1,
        ///     i.e. always switches — today's behavior) at liveliness ≥ 1; lower liveliness makes
        ///     a character read as more "stuck" on one gesture.
        /// </summary>
        public static float ResolveVariantSwitchProbability(ConvaiBodyAnimationConfig config) =>
            Mathf.Clamp01(config.GestureLiveliness);

        /// <summary>
        ///     Effective beat-gesture refractory window: <paramref name="baseRefractorySeconds" />
        ///     divided by liveliness (clamped to a sane floor so the divisor never explodes the
        ///     rate). Neutral (identity) at liveliness = 1.
        /// </summary>
        public static float ResolveBeatRefractorySeconds(ConvaiBodyAnimationConfig config, float baseRefractorySeconds) =>
            baseRefractorySeconds / Mathf.Max(MinLivelinessForRefractory, config.GestureLiveliness);

        /// <summary>Multiplier on idle-variant-interval min/max. Neutral (1) at calmness = 1.</summary>
        public static float ResolveIdleIntervalScale(ConvaiBodyAnimationConfig config) =>
            Mathf.Clamp(config.Calmness, 0f, 2f);

        /// <summary>
        ///     Multiplier on the talk/listen envelope fade-in duration: <c>1 + 0.25 * (calmness - 1)</c>,
        ///     clamped to a sane range. Neutral (1) at calmness = 1.
        /// </summary>
        public static float ResolveTalkFadeInScale(ConvaiBodyAnimationConfig config) =>
            Mathf.Clamp(1f + 0.25f * (config.Calmness - 1f), 0.25f, 2.5f);
    }
}
