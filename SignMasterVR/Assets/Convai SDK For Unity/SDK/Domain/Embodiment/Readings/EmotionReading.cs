using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Convai.Domain.Embodiment.Readings
{
    /// <summary>
    ///     Immutable snapshot of the character's current composed emotion state produced by
    ///     <see cref="IEmotionStateSource" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The reading is the output of the emotion pipeline AFTER taxonomy resolution,
    ///         smoothing, micro-expression bursts, and neutral alternation have been applied.
    ///         Downstream consumers (body language, facial animation, LipSync mouth fade)
    ///         treat it as the authoritative current-frame emotion.
    ///     </para>
    ///     <para>
    ///         <see cref="AllScores" /> is keyed by canonical lowercase labels resolved from
    ///         <see cref="IEmotionTaxonomy" /> (e.g. <c>"joy"</c>, <c>"anger"</c>, <c>"neutral"</c>).
    ///         The score table is defensively copied when the reading is created, so public
    ///         SDK consumers may safely hold a reading beyond the current frame.
    ///     </para>
    /// </remarks>
    public readonly struct EmotionReading
    {
        /// <summary>
        ///     Canonical label of the highest-scoring emotion this frame. Falls back to
        ///     <c>"neutral"</c> when no emotion is active.
        /// </summary>
        public string DominantLabel { get; }

        /// <summary>Normalized <c>[0, 1]</c> score for <see cref="DominantLabel" />.</summary>
        public float DominantScore { get; }

        /// <summary>
        ///     Score table for every emotion the taxonomy recognizes. Values are normalized to
        ///     <c>[0, 1]</c>. The table always includes the full taxonomy; emotions with no
        ///     contribution this frame have a score of <c>0</c>.
        /// </summary>
        public IReadOnlyDictionary<string, float> AllScores { get; }

        /// <summary>
        ///     Hint for how much the emotion wants to shape the mouth during lip-sync handoff.
        ///     Range <c>[0, 1]</c>. LipSync uses this as the fade-out target so the mouth
        ///     eases into the current expression instead of snapping to neutral at the end of
        ///     a speech chunk.
        /// </summary>
        public float MouthInfluence { get; }

        /// <summary>
        ///     Wall-clock time (in seconds) the dominant emotion has been held. Useful for
        ///     micro-expression logic that wants to suppress bursts while an emotion is stable.
        /// </summary>
        public float DominantHoldSeconds { get; }

        /// <summary>
        ///     Canonical label of the character's persona/temperament resting mood (e.g. a "warm"
        ///     character resting at a low-intensity <c>joy</c>). This is explicitly NOT the
        ///     dominant emotion — a transient server-driven emotion can be active while the mood
        ///     stays unchanged underneath it. Falls back to <c>"neutral"</c> when no baseline is
        ///     configured.
        /// </summary>
        public string MoodLabel { get; }

        /// <summary>Normalized <c>[0, 1]</c> intensity for <see cref="MoodLabel" />.</summary>
        public float MoodScore { get; }

        public EmotionReading(
            string dominantLabel,
            float dominantScore,
            IReadOnlyDictionary<string, float> allScores,
            float mouthInfluence,
            float dominantHoldSeconds)
            : this(dominantLabel, dominantScore, allScores, mouthInfluence, dominantHoldSeconds,
                NeutralLabel, 0f)
        {
        }

        /// <summary>
        ///     Constructs a reading that additionally carries the persona/temperament resting mood
        ///     (<see cref="MoodLabel" />/<see cref="MoodScore" />), separate from the transient
        ///     <see cref="DominantLabel" />/<see cref="DominantScore" />.
        /// </summary>
        public EmotionReading(
            string dominantLabel,
            float dominantScore,
            IReadOnlyDictionary<string, float> allScores,
            float mouthInfluence,
            float dominantHoldSeconds,
            string moodLabel,
            float moodScore)
        {
            DominantLabel = string.IsNullOrEmpty(dominantLabel) ? NeutralLabel : dominantLabel;
            DominantScore = dominantScore < 0f ? 0f : dominantScore > 1f ? 1f : dominantScore;
            AllScores = FreezeScores(allScores);
            MouthInfluence = mouthInfluence < 0f ? 0f : mouthInfluence > 1f ? 1f : mouthInfluence;
            DominantHoldSeconds = dominantHoldSeconds < 0f ? 0f : dominantHoldSeconds;
            MoodLabel = string.IsNullOrEmpty(moodLabel) ? NeutralLabel : moodLabel;
            MoodScore = moodScore < 0f ? 0f : moodScore > 1f ? 1f : moodScore;
        }

        /// <summary>Canonical label used when no emotion is active.</summary>
        public const string NeutralLabel = "neutral";

        /// <summary>Static empty score table used as a safe default.</summary>
        public static readonly IReadOnlyDictionary<string, float> EmptyScores =
            new ReadOnlyDictionary<string, float>(new Dictionary<string, float>(0));

        /// <summary>Neutral reading; emotion pipeline produces this on startup and shutdown.</summary>
        public static EmotionReading Neutral => new(NeutralLabel, 0f, EmptyScores, 0f, 0f);

        /// <summary>
        ///     Returns <c>true</c> when the dominant emotion is neutral (either explicitly or by
        ///     absence of any non-neutral contribution).
        /// </summary>
        public bool IsNeutral =>
            string.Equals(DominantLabel, NeutralLabel, System.StringComparison.OrdinalIgnoreCase) ||
            DominantScore <= 0f;

        /// <summary>
        ///     Copies the score table into a caller-owned dictionary without exposing the
        ///     reading's immutable backing store.
        /// </summary>
        public void CopyScoresTo(IDictionary<string, float> destination)
        {
            if (destination == null) return;
            destination.Clear();
            foreach (KeyValuePair<string, float> kvp in AllScores)
                destination[kvp.Key] = kvp.Value;
        }

        /// <summary>Returns a score by canonical label, or <c>0</c> when absent.</summary>
        public float GetScore(string canonicalLabel)
        {
            if (string.IsNullOrEmpty(canonicalLabel)) return 0f;
            return AllScores.TryGetValue(canonicalLabel, out float score) ? score : 0f;
        }

        private static IReadOnlyDictionary<string, float> FreezeScores(IReadOnlyDictionary<string, float> scores)
        {
            if (scores == null || scores.Count == 0)
                return EmptyScores;

            Dictionary<string, float> copy = new(scores.Count, System.StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, float> kvp in scores)
                copy[kvp.Key] = kvp.Value < 0f ? 0f : kvp.Value > 1f ? 1f : kvp.Value;
            return new ReadOnlyDictionary<string, float>(copy);
        }
    }
}
