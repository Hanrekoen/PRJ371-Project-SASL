using System;
using System.Collections.Generic;
using Convai.Domain.Embodiment.Taxonomy;
using Convai.Modules.Emotion.Outputs;
using Convai.Modules.Emotion.Profiles;
using Convai.Modules.Emotion.Taxonomy;

namespace Convai.Modules.Emotion.Editor
{
    /// <summary>
    ///     Pure, GUI-free label validation for <see cref="ConvaiEmotionProfile" /> authoring
    ///     surfaces: Persona Baseline, Expressiveness, Per-Emotion Dynamics, and Output Bindings
    ///     all reference emotion labels by free-text string, so a typo silently produces a
    ///     resolve-to-neutral/no-op at runtime with no authoring-time signal. This helper flags
    ///     labels absent from the profile's effective vocabulary and suggests the closest known
    ///     label via a small case-insensitive Levenshtein distance.
    /// </summary>
    /// <remarks>
    ///     No <c>ScriptableObject</c> is ever created here; when the profile has no assigned
    ///     <see cref="ConvaiEmotionProfile.Taxonomy" />, the effective vocabulary falls back to a
    ///     hardcoded label set mirroring <see cref="EmotionTaxonomyAsset.CreateDefault" /> (locked
    ///     to that method by a drift-guard test) instead of synthesizing the real default asset.
    /// </remarks>
    internal static class EmotionProfileValidation
    {
        /// <summary>Which authoring surface an offending label came from.</summary>
        internal enum FindingCategory
        {
            Baseline,
            Expressiveness,
            Dynamics,
            SemanticRecipes,
            MaterialSlots
        }

        /// <summary>One unknown-label result.</summary>
        internal readonly struct Finding
        {
            /// <summary>Authoring surface this label came from.</summary>
            internal readonly FindingCategory Category;

            /// <summary>The offending, author-entered label text (trimmed).</summary>
            internal readonly string Label;

            /// <summary>
            ///     Nearest known vocabulary label within edit distance 2, or <c>null</c>/empty
            ///     when no candidate is close enough to suggest.
            /// </summary>
            internal readonly string Suggestion;

            /// <summary>List index the label came from, or <c>-1</c> when not list-based (Baseline).</summary>
            internal readonly int Index;

            internal Finding(FindingCategory category, string label, string suggestion, int index)
            {
                Category = category;
                Label = label;
                Suggestion = suggestion;
                Index = index;
            }
        }

        /// <summary>
        ///     Hardcoded mirror of <see cref="EmotionTaxonomyAsset.CreateDefault" />'s canonical
        ///     labels, used only when a profile has no assigned taxonomy. A unit test locks this
        ///     array to the real default so the two cannot silently drift apart.
        /// </summary>
        private const int MaxSuggestionDistance = 2;

        internal static readonly string[] DefaultVocabularyLabels =
        {
            "neutral",
            "joy",
            "trust",
            "fear",
            "surprise",
            "sadness",
            "disgust",
            "anger",
            "anticipation"
        };

        /// <summary>
        ///     Validates every free-text emotion label on <paramref name="profile" /> against its
        ///     effective vocabulary, appending one <see cref="Finding" /> per distinct unknown
        ///     (category, label) pair into the caller-owned <paramref name="results" /> list.
        /// </summary>
        /// <param name="profile">Profile to validate. A <c>null</c> profile produces no findings.</param>
        /// <param name="results">
        ///     Caller-owned list, cleared at the start of this call so it can be reused across
        ///     inspector repaints without a new allocation per call.
        /// </param>
        internal static void Validate(ConvaiEmotionProfile profile, List<Finding> results)
        {
            if (results == null) return;
            results.Clear();
            if (profile == null) return;

            EmotionTaxonomyAsset taxonomy = profile.Taxonomy;

            string baseline = profile.BaselineEmotionLabel;
            if (!string.IsNullOrWhiteSpace(baseline))
                CheckLabel(taxonomy, FindingCategory.Baseline, baseline, -1, results);

            IReadOnlyList<EmotionExpressivenessEntry> expressiveness = profile.Expressiveness;
            if (expressiveness != null)
            {
                for (int i = 0; i < expressiveness.Count; i++)
                {
                    EmotionExpressivenessEntry entry = expressiveness[i];
                    if (entry == null || string.IsNullOrWhiteSpace(entry.Label)) continue;
                    CheckLabel(taxonomy, FindingCategory.Expressiveness, entry.Label, i, results);
                }
            }

            IReadOnlyList<EmotionDynamicsEntry> dynamics = profile.EmotionDynamics;
            if (dynamics != null)
            {
                for (int i = 0; i < dynamics.Count; i++)
                {
                    EmotionDynamicsEntry entry = dynamics[i];
                    if (entry == null || string.IsNullOrWhiteSpace(entry.Label)) continue;
                    CheckLabel(taxonomy, FindingCategory.Dynamics, entry.Label, i, results);
                }
            }

            IReadOnlyList<EmotionExpressionRecipe> recipes = profile.ExpressionRecipes;
            if (recipes != null)
            {
                for (int i = 0; i < recipes.Count; i++)
                {
                    EmotionExpressionRecipe recipe = recipes[i];
                    if (recipe == null || string.IsNullOrWhiteSpace(recipe.EmotionLabel)) continue;
                    CheckLabel(taxonomy, FindingCategory.SemanticRecipes, recipe.EmotionLabel, i, results);
                }
            }

            IReadOnlyList<MaterialPropertyEmotionSlot> materialSlots = profile.MaterialBinding?.Slots;
            if (materialSlots != null)
            {
                for (int i = 0; i < materialSlots.Count; i++)
                {
                    MaterialPropertyEmotionSlot slot = materialSlots[i];
                    if (slot == null) continue;
                    if (string.IsNullOrWhiteSpace(slot.EmotionLabel)) continue;
                    if (string.IsNullOrWhiteSpace(slot.PropertyName)) continue; // no payload
                    CheckLabel(taxonomy, FindingCategory.MaterialSlots, slot.EmotionLabel, i, results);
                }
            }
        }

        private static void CheckLabel(
            EmotionTaxonomyAsset taxonomy, FindingCategory category, string label, int index, List<Finding> results)
        {
            string trimmed = label.Trim();
            if (ResolvesInVocabulary(taxonomy, trimmed)) return;

            // At most one finding per distinct (category, label) pair, so a label repeated across
            // many slots is reported once rather than once per slot.
            for (int i = 0; i < results.Count; i++)
            {
                Finding existing = results[i];
                if (existing.Category == category &&
                    string.Equals(existing.Label, trimmed, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            string suggestion = FindSuggestion(taxonomy, trimmed);
            results.Add(new Finding(category, trimmed, suggestion, index));
        }

        /// <summary>
        ///     Resolves <paramref name="label" /> against the profile's effective vocabulary:
        ///     <paramref name="taxonomy" />'s labels and aliases (case-insensitive) when assigned,
        ///     otherwise <see cref="DefaultVocabularyLabels" />.
        /// </summary>
        private static bool ResolvesInVocabulary(EmotionTaxonomyAsset taxonomy, string label)
        {
            if (taxonomy != null)
                return taxonomy.TryResolve(label, out _);

            for (int i = 0; i < DefaultVocabularyLabels.Length; i++)
                if (string.Equals(DefaultVocabularyLabels[i], label, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        /// <summary>
        ///     Finds the vocabulary label with the smallest case-insensitive Levenshtein distance
        ///     to <paramref name="label" />, or <c>null</c> when the closest candidate is farther
        ///     than <see cref="MaxSuggestionDistance" />. Ties resolve to the first candidate in
        ///     vocabulary order.
        /// </summary>
        private static string FindSuggestion(EmotionTaxonomyAsset taxonomy, string label)
        {
            int bestDistance = int.MaxValue;
            string best = null;

            if (taxonomy != null)
            {
                IReadOnlyList<EmotionDescriptor> emotions = taxonomy.Emotions;
                for (int i = 0; i < emotions.Count; i++)
                {
                    EmotionDescriptor descriptor = emotions[i];
                    if (descriptor == null || string.IsNullOrWhiteSpace(descriptor.Label)) continue;
                    int distance = LevenshteinDistanceIgnoreCase(label, descriptor.Label);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = descriptor.Label;
                    }
                }
            }
            else
            {
                for (int i = 0; i < DefaultVocabularyLabels.Length; i++)
                {
                    int distance = LevenshteinDistanceIgnoreCase(label, DefaultVocabularyLabels[i]);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = DefaultVocabularyLabels[i];
                    }
                }
            }

            return bestDistance <= MaxSuggestionDistance ? best : null;
        }

        /// <summary>
        ///     Case-insensitive Levenshtein (edit) distance using a two-row <c>int[]</c> buffer.
        ///     Editor-only tooling code, called on demand (not per-frame), so the small per-call
        ///     buffer allocation is acceptable.
        /// </summary>
        private static int LevenshteinDistanceIgnoreCase(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) return string.IsNullOrEmpty(b) ? 0 : b.Length;
            if (string.IsNullOrEmpty(b)) return a.Length;

            int lengthA = a.Length;
            int lengthB = b.Length;
            var previousRow = new int[lengthB + 1];
            var currentRow = new int[lengthB + 1];

            for (int j = 0; j <= lengthB; j++)
                previousRow[j] = j;

            for (int i = 1; i <= lengthA; i++)
            {
                currentRow[0] = i;
                char charA = char.ToLowerInvariant(a[i - 1]);

                for (int j = 1; j <= lengthB; j++)
                {
                    char charB = char.ToLowerInvariant(b[j - 1]);
                    int cost = charA == charB ? 0 : 1;

                    int deletion = previousRow[j] + 1;
                    int insertion = currentRow[j - 1] + 1;
                    int substitution = previousRow[j - 1] + cost;

                    int min = deletion < insertion ? deletion : insertion;
                    currentRow[j] = min < substitution ? min : substitution;
                }

                (previousRow, currentRow) = (currentRow, previousRow);
            }

            return previousRow[lengthB];
        }
    }
}
