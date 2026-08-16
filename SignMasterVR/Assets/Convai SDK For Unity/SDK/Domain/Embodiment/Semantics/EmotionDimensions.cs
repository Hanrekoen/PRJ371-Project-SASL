using System;

namespace Convai.Domain.Embodiment.Semantics
{
    // Stays public, deliberately: this is data carried inside public reading structs
    // (EmotionStateFrame, EmotionDescriptor, BodyLanguageReading). Internalizing it would
    // force those readings internal too, which buys nothing — a customer reading a
    // character's emotion legitimately sees this shape.
    /// <summary>
    /// Continuous affect dimensions shared by Emotion, Gaze, Body Language and locomotion.
    /// Categorical labels remain authoritative for authored facial recipes; these dimensions
    /// provide one coherent modulation signal for blending and cross-module behavior.
    /// </summary>
    public readonly struct EmotionDimensions
    {
        public EmotionDimensions(float valence, float arousal, float agency, float approach)
        {
            Valence = ClampSigned(valence);
            Arousal = ClampSigned(arousal);
            Agency = ClampSigned(agency);
            Approach = ClampSigned(approach);
        }

        public float Valence { get; }
        public float Arousal { get; }
        public float Agency { get; }
        public float Approach { get; }

        public static EmotionDimensions Neutral => default;

        public static EmotionDimensions Lerp(in EmotionDimensions from, in EmotionDimensions to, float t)
        {
            t = Clamp01(t);
            return new EmotionDimensions(
                from.Valence + (to.Valence - from.Valence) * t,
                from.Arousal + (to.Arousal - from.Arousal) * t,
                from.Agency + (to.Agency - from.Agency) * t,
                from.Approach + (to.Approach - from.Approach) * t);
        }

        private static float ClampSigned(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            return value < -1f ? -1f : value > 1f ? 1f : value;
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value)) return 0f;
            return value < 0f ? 0f : value > 1f ? 1f : value;
        }
    }

    /// <summary>Conservative defaults for the SDK's built-in Plutchik-style vocabulary.</summary>
    public static class EmotionDimensionDefaults
    {
        public static EmotionDimensions Resolve(string canonicalLabel)
        {
            if (string.IsNullOrEmpty(canonicalLabel)) return EmotionDimensions.Neutral;

            if (EqualsLabel(canonicalLabel, "joy"))          return new EmotionDimensions(0.9f, 0.65f, 0.45f, 0.8f);
            if (EqualsLabel(canonicalLabel, "trust"))        return new EmotionDimensions(0.7f, 0.15f, 0.25f, 0.7f);
            if (EqualsLabel(canonicalLabel, "fear"))         return new EmotionDimensions(-0.8f, 0.9f, -0.75f, -0.9f);
            if (EqualsLabel(canonicalLabel, "surprise"))     return new EmotionDimensions(0.1f, 0.95f, -0.1f, 0f);
            if (EqualsLabel(canonicalLabel, "sadness"))      return new EmotionDimensions(-0.85f, -0.45f, -0.65f, -0.55f);
            if (EqualsLabel(canonicalLabel, "disgust"))      return new EmotionDimensions(-0.75f, 0.35f, 0.35f, -0.65f);
            if (EqualsLabel(canonicalLabel, "anger"))        return new EmotionDimensions(-0.8f, 0.85f, 0.8f, 0.9f);
            if (EqualsLabel(canonicalLabel, "anticipation")) return new EmotionDimensions(0.3f, 0.55f, 0.35f, 0.6f);
            return EmotionDimensions.Neutral;
        }

        private static bool EqualsLabel(string value, string expected) =>
            string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
    }
}
