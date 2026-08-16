using System;
using System.Collections.Generic;
using Convai.Domain.Embodiment.Semantics;

namespace Convai.Domain.Embodiment.Readings
{
    /// <summary>
    /// Borrowed zero-allocation view of the current emotion state. The label and score lists are
    /// owned by the source and remain valid until that source rebuilds; callers that need retained
    /// immutable data should use the <see cref="EmotionReading"/> snapshot API.
    /// </summary>
    public readonly struct EmotionStateFrame
    {
        public EmotionStateFrame(
            int version,
            string dominantLabel,
            float dominantScore,
            IReadOnlyList<string> labels,
            IReadOnlyList<float> scores,
            string moodLabel,
            float moodScore,
            EmotionDimensions dimensions,
            float mouthInfluence,
            float dominantHoldSeconds)
        {
            Version = version;
            DominantLabel = string.IsNullOrEmpty(dominantLabel) ? EmotionReading.NeutralLabel : dominantLabel;
            DominantScore = Clamp01(dominantScore);
            Labels = labels ?? Array.Empty<string>();
            Scores = scores ?? Array.Empty<float>();
            MoodLabel = string.IsNullOrEmpty(moodLabel) ? EmotionReading.NeutralLabel : moodLabel;
            MoodScore = Clamp01(moodScore);
            Dimensions = dimensions;
            MouthInfluence = Clamp01(mouthInfluence);
            DominantHoldSeconds = dominantHoldSeconds < 0f ? 0f : dominantHoldSeconds;
        }

        public int Version { get; }
        public string DominantLabel { get; }
        public float DominantScore { get; }
        public IReadOnlyList<string> Labels { get; }
        public IReadOnlyList<float> Scores { get; }
        public string MoodLabel { get; }
        public float MoodScore { get; }
        public EmotionDimensions Dimensions { get; }
        public float MouthInfluence { get; }
        public float DominantHoldSeconds { get; }

        public bool IsNeutral => DominantScore <= 0f ||
            string.Equals(DominantLabel, EmotionReading.NeutralLabel, StringComparison.OrdinalIgnoreCase);

        public float GetScore(int index) =>
            Scores != null && index >= 0 && index < Scores.Count ? Clamp01(Scores[index]) : 0f;

        public float GetScore(string canonicalLabel)
        {
            if (string.IsNullOrEmpty(canonicalLabel) || Labels == null || Scores == null) return 0f;
            int count = Math.Min(Labels.Count, Scores.Count);
            for (int i = 0; i < count; i++)
            {
                if (string.Equals(Labels[i], canonicalLabel, StringComparison.OrdinalIgnoreCase))
                    return Clamp01(Scores[i]);
            }
            return 0f;
        }

        public static EmotionStateFrame Neutral => new(
            0, EmotionReading.NeutralLabel, 0f, Array.Empty<string>(), Array.Empty<float>(),
            EmotionReading.NeutralLabel, 0f, EmotionDimensions.Neutral, 0f, 0f);

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value)) return 0f;
            return value < 0f ? 0f : value > 1f ? 1f : value;
        }
    }
}
