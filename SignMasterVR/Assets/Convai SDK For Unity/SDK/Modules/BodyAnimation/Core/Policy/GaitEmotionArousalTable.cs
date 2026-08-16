using System;

namespace Convai.Modules.BodyAnimation.Core.Policy
{
    /// <summary>
    ///     Static arousal-only lookup for the emotion labels the SDK's default taxonomy ships
    ///     (<c>EmotionTaxonomyAsset.BuildDefault</c>: neutral plus Plutchik's eight). Backs
    ///     <see cref="EmotionGaitModulator" />: high arousal reads as a faster, more
    ///     energized gait, low/negative arousal as a slower, heavier one.
    /// </summary>
    /// <remarks>
    ///     Values are the conventional circumplex-model (Russell, 1980) arousal placements used
    ///     elsewhere in the SDK's affective mapping (see the Body Language module's own
    ///     valence/arousal table) — reproduced here rather than referenced, since modules never
    ///     reference each other. Only arousal is needed for gait speed, so valence is omitted.
    /// </remarks>
    internal static class GaitEmotionArousalTable
    {
        /// <summary>
        ///     Resolves the arousal placement (-1 calm .. 1 activated) for a taxonomy label
        ///     (case-insensitive, allocation-free). Returns <c>false</c> for an unrecognized
        ///     label, in which case the caller must treat the emotion as neutral (multiplier 1).
        /// </summary>
        public static bool TryGetArousal(string label, out float arousal)
        {
            if (string.IsNullOrEmpty(label))
            {
                arousal = 0f;
                return false;
            }

            if (Matches(label, "neutral")) { arousal = 0f; return true; }
            if (Matches(label, "joy")) { arousal = 0.55f; return true; }
            if (Matches(label, "trust")) { arousal = -0.2f; return true; }
            if (Matches(label, "fear")) { arousal = 0.7f; return true; }
            if (Matches(label, "surprise")) { arousal = 0.85f; return true; }
            if (Matches(label, "sadness")) { arousal = -0.4f; return true; }
            if (Matches(label, "disgust")) { arousal = 0.2f; return true; }
            if (Matches(label, "anger")) { arousal = 0.75f; return true; }
            if (Matches(label, "anticipation")) { arousal = 0.4f; return true; }

            arousal = 0f;
            return false;
        }

        private static bool Matches(string label, string candidate) =>
            string.Equals(label, candidate, StringComparison.OrdinalIgnoreCase);
    }
}
