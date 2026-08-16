using System;
using System.Collections.Generic;
using Convai.Domain.Embodiment.Taxonomy;
using Convai.Modules.Emotion.Profiles;

namespace Convai.Modules.Emotion.Compilation
{
    internal sealed class CompiledEmotionModel
    {
        internal readonly string[] Labels;
        internal readonly CompiledExpressionRecipe[] Recipes;

        internal CompiledEmotionModel(string[] labels, CompiledExpressionRecipe[] recipes)
        {
            Labels = labels ?? Array.Empty<string>();
            Recipes = recipes ?? Array.Empty<CompiledExpressionRecipe>();
        }
    }

    internal readonly struct CompiledExpressionRecipe
    {
        internal CompiledExpressionRecipe(int emotionIndex, float onsetSeconds, float holdSeconds,
            float releaseSeconds, float maximumIntensity, EmotionExpressionChannel[] channels)
        {
            EmotionIndex = emotionIndex;
            OnsetSeconds = onsetSeconds;
            HoldSeconds = holdSeconds;
            ReleaseSeconds = releaseSeconds;
            MaximumIntensity = maximumIntensity;
            Channels = channels ?? Array.Empty<EmotionExpressionChannel>();
        }

        internal int EmotionIndex { get; }
        internal float OnsetSeconds { get; }
        internal float HoldSeconds { get; }
        internal float ReleaseSeconds { get; }
        internal float MaximumIntensity { get; }
        internal EmotionExpressionChannel[] Channels { get; }
    }

    internal static class EmotionProfileCompiler
    {
        internal static CompiledEmotionModel Compile(IEmotionTaxonomy taxonomy,
            IReadOnlyList<EmotionExpressionRecipe> authoredRecipes)
        {
            if (taxonomy == null) return new CompiledEmotionModel(null, null);

            int count = taxonomy.Emotions.Count;
            var labels = new string[count];
            var indices = new Dictionary<string, int>(count, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < count; i++)
            {
                labels[i] = taxonomy.Emotions[i].Label;
                indices[labels[i]] = i;
            }

            IReadOnlyList<EmotionExpressionRecipe> recipes = authoredRecipes;
            List<EmotionExpressionRecipe> defaults = null;
            if (recipes == null || recipes.Count == 0)
            {
                defaults = EmotionExpressionRecipe.CreateDefaultSet();
                recipes = defaults;
            }

            var compiled = new List<CompiledExpressionRecipe>(recipes.Count);
            for (int i = 0; i < recipes.Count; i++)
            {
                EmotionExpressionRecipe recipe = recipes[i];
                if (recipe == null || string.IsNullOrWhiteSpace(recipe.EmotionLabel)) continue;
                if (!taxonomy.TryResolve(recipe.EmotionLabel, out EmotionDescriptor descriptor)) continue;
                if (!indices.TryGetValue(descriptor.Label, out int emotionIndex) || descriptor.IsNeutral) continue;

                IReadOnlyList<EmotionExpressionChannel> sourceChannels = recipe.Channels;
                var channels = new List<EmotionExpressionChannel>(sourceChannels?.Count ?? 0);
                if (sourceChannels != null)
                {
                    for (int c = 0; c < sourceChannels.Count; c++)
                        if (sourceChannels[c] != null) channels.Add(sourceChannels[c]);
                }

                compiled.Add(new CompiledExpressionRecipe(
                    emotionIndex,
                    Math.Max(0.03f, recipe.OnsetSeconds),
                    Math.Max(0f, recipe.ApexHoldSeconds),
                    Math.Max(0.05f, recipe.ReleaseSeconds),
                    Clamp01(recipe.MaximumIntensity),
                    channels.ToArray()));
            }

            return new CompiledEmotionModel(labels, compiled.ToArray());
        }

        private static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;
    }
}
