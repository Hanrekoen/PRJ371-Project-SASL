using System;
using Convai.Runtime.Utilities;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Data
{
    /// <summary>
    ///     Optional selection bias tying a clip variant to an emotion label. When the
    ///     character's dominant emotion matches <see cref="EmotionLabel" />, the variant's
    ///     selection weight is multiplied by <see cref="WeightMultiplier" /> scaled by the
    ///     emotion's current score.
    /// </summary>
    /// <remarks>
    ///     Labels are matched case-insensitively against the canonical lowercase labels of
    ///     the emotion taxonomy (e.g. <c>"joy"</c>, <c>"anger"</c>, <c>"neutral"</c>).
    /// </remarks>
    [Serializable]
    public sealed class EmotionAffinity
    {
        [SerializeField, ConvaiEmotionLabel]
        [Tooltip("The emotion this affinity reacts to.")]
        private string _emotionLabel;

        [SerializeField]
        [Min(0f)]
        [Tooltip("Multiplier applied to the variant's weight when the emotion is dominant. " +
                 "Values above 1 favor the variant, values below 1 suppress it, 0 excludes it.")]
        private float _weightMultiplier = 2f;

        public string EmotionLabel => _emotionLabel;
        public float WeightMultiplier => _weightMultiplier;

        /// <summary>
        ///     Resolves the effective weight multiplier for the given dominant emotion.
        ///     Returns 1 (no bias) when the labels do not match; otherwise interpolates from
        ///     1 toward <see cref="WeightMultiplier" /> by the emotion score so weak emotions
        ///     bias selection weakly.
        /// </summary>
        public float Evaluate(string dominantLabel, float dominantScore)
        {
            if (string.IsNullOrEmpty(_emotionLabel) || string.IsNullOrEmpty(dominantLabel))
                return 1f;
            if (!string.Equals(_emotionLabel, dominantLabel, StringComparison.OrdinalIgnoreCase))
                return 1f;

            return Mathf.LerpUnclamped(1f, _weightMultiplier, Mathf.Clamp01(dominantScore));
        }

        internal void Initialize(string emotionLabel, float weightMultiplier)
        {
            _emotionLabel = emotionLabel;
            _weightMultiplier = Mathf.Max(0f, weightMultiplier);
        }
    }
}
