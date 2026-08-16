using UnityEngine;

namespace Convai.Modules.BodyLanguage.Core.Policy
{
    /// <summary>
    ///     Static valence/arousal lookup and derivation for the emotion labels the SDK's
    ///     emotion taxonomy ships by default (<c>EmotionTaxonomyAsset.BuildDefault</c>:
    ///     neutral plus Plutchik's eight — joy, trust, fear, surprise, sadness, disgust, anger,
    ///     anticipation). Backs <see cref="EmotionBodyModulator" />'s fallback path for any
    ///     label not hand-tuned in the profile's emotion table.
    /// </summary>
    /// <remarks>
    ///     Values are the conventional circumplex-model placements (Russell, 1980) used
    ///     throughout affective computing literature: valence -1 (unpleasant) .. 1 (pleasant),
    ///     arousal -1 (calm) .. 1 (activated). The derivation maps valence to openness/lean and
    ///     arousal to shoulder tension/gesture rate/breath rate, matching the qualitative shape
    ///     of the big-six hand-tuned rows in <c>ConvaiBodyLanguageProfile.BuildDefaultEmotionModifiers</c>
    ///     (e.g. high-valence/low-arousal joy opens and relaxes the shoulders; low-valence/
    ///     high-arousal fear retracts and tenses) so labels without a hand-tuned row still read
    ///     consistently with the ones that have one.
    /// </remarks>
    internal static class EmotionValenceArousalTable
    {
        /// <summary>
        ///     Resolves the canonical valence/arousal placement for a taxonomy label
        ///     (case-insensitive, allocation-free — compares ordinally rather than
        ///     normalizing the input string, since the emotion pipeline already canonicalizes
        ///     labels to lowercase before they reach this table).
        /// </summary>
        public static bool TryGetValenceArousal(string label, out float valence, out float arousal)
        {
            if (string.IsNullOrEmpty(label))
            {
                valence = 0f; arousal = 0f; return false;
            }

            if (Matches(label, "neutral")) { valence = 0f; arousal = 0f; return true; }
            if (Matches(label, "joy")) { valence = 0.85f; arousal = 0.55f; return true; }
            if (Matches(label, "trust")) { valence = 0.6f; arousal = -0.2f; return true; }
            if (Matches(label, "fear")) { valence = -0.75f; arousal = 0.7f; return true; }
            if (Matches(label, "surprise")) { valence = 0.15f; arousal = 0.85f; return true; }
            if (Matches(label, "sadness")) { valence = -0.7f; arousal = -0.4f; return true; }
            if (Matches(label, "disgust")) { valence = -0.6f; arousal = 0.2f; return true; }
            if (Matches(label, "anger")) { valence = -0.5f; arousal = 0.75f; return true; }
            if (Matches(label, "anticipation")) { valence = 0.35f; arousal = 0.4f; return true; }

            valence = 0f; arousal = 0f; return false;
        }

        private static bool Matches(string label, string candidate) =>
            string.Equals(label, candidate, System.StringComparison.OrdinalIgnoreCase);

        /// <summary>
        ///     Derives a body-language modifier from a valence/arousal placement: valence
        ///     drives openness and lean (pleasant opens and leans in, unpleasant closes and
        ///     retracts), arousal drives shoulder tension and gesture/breath rate (activated
        ///     tenses and speeds up, calm relaxes and slows). Gesture intensity blends both —
        ///     an activated, pleasant state gestures the most; a calm, unpleasant one the least.
        /// </summary>
        public static void DeriveModifier(
            float valence, float arousal,
            out float opennessBias, out float leanBias, out float shoulderTensionBias,
            out float gestureIntensityScale, out float gestureRateScale,
            out float breathRateScale, out float breathDepthScale)
        {
            valence = Mathf.Clamp(valence, -1f, 1f);
            arousal = Mathf.Clamp(arousal, -1f, 1f);

            opennessBias = valence * 0.6f;
            leanBias = valence * 0.3f + arousal * 0.15f;
            shoulderTensionBias = arousal * 0.6f - valence * 0.15f;

            gestureIntensityScale = Mathf.Clamp(1f + arousal * 0.35f + valence * 0.15f, 0f, 2f);
            gestureRateScale = Mathf.Clamp(1f + arousal * 0.3f, 0f, 2f);
            breathRateScale = Mathf.Clamp(1f + arousal * 0.25f, 0f, 2f);
            breathDepthScale = Mathf.Clamp(1f + valence * 0.15f + arousal * 0.1f, 0f, 2f);

            opennessBias = Mathf.Clamp(opennessBias, -1f, 1f);
            leanBias = Mathf.Clamp(leanBias, -1f, 1f);
            shoulderTensionBias = Mathf.Clamp(shoulderTensionBias, -1f, 1f);
        }
    }
}
