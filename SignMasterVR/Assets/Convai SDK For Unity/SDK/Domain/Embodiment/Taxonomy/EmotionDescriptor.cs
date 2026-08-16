using System.Collections.Generic;
using Convai.Domain.Embodiment.Semantics;

namespace Convai.Domain.Embodiment.Taxonomy
{
    /// <summary>
    ///     Declarative description of a single emotion within an <see cref="IEmotionTaxonomy" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Descriptors are pure data: they do not know how an emotion is rendered, only how
    ///         the pipeline should resolve, score, and decay it. Rendering concerns (blendshape
    ///         names, animator parameters, mouth weight curves) live in the <c>ConvaiEmotionProfile</c>
    ///         asset that applies this taxonomy.
    ///     </para>
    ///     <para>
    ///         The descriptor is immutable once constructed so that the taxonomy asset can be
    ///         safely exposed as a read-only view.
    ///     </para>
    /// </remarks>
    public sealed class EmotionDescriptor
    {
        /// <summary>
        ///     Canonical lowercase label used across the runtime (e.g. <c>"joy"</c>,
        ///     <c>"neutral"</c>). Must be unique within the taxonomy.
        /// </summary>
        public string Label { get; }

        /// <summary>
        ///     Aliases accepted during server-label resolution (e.g. <c>"happy"</c> → <c>"joy"</c>).
        ///     Case-insensitive.
        /// </summary>
        public IReadOnlyList<string> Aliases { get; }

        /// <summary>
        ///     Labels of emotions that compose this emotion's neutral base. When these are
        ///     present they suppress neutral synthesis. Mirrors the behavior of
        ///     <see cref="IEmotionTaxonomy.Neutral" />'s complement set.
        /// </summary>
        public IReadOnlyList<string> Complements { get; }

        /// <summary>
        ///     Default mouth-shape influence this emotion wants during non-speaking beats in
        ///     <c>[0, 1]</c>. Profiles may override but the taxonomy encodes a sensible default.
        /// </summary>
        public float DefaultMouthInfluence { get; }

        /// <summary>
        ///     Marks this descriptor as the taxonomy's neutral baseline. Exactly one descriptor
        ///     per taxonomy must have this flag set.
        /// </summary>
        public bool IsNeutral { get; }

        /// <summary>Continuous affect dimensions used for cross-module blending.</summary>
        public EmotionDimensions Dimensions { get; }

        public EmotionDescriptor(
            string label,
            IReadOnlyList<string> aliases,
            IReadOnlyList<string> complements,
            float defaultMouthInfluence,
            bool isNeutral)
            : this(label, aliases, complements, defaultMouthInfluence, isNeutral,
                EmotionDimensionDefaults.Resolve(label))
        {
        }

        public EmotionDescriptor(
            string label,
            IReadOnlyList<string> aliases,
            IReadOnlyList<string> complements,
            float defaultMouthInfluence,
            bool isNeutral,
            EmotionDimensions dimensions)
        {
            if (string.IsNullOrWhiteSpace(label))
                throw new System.ArgumentException("Emotion label must be non-empty.", nameof(label));

            Label = label.Trim().ToLowerInvariant();
            Aliases = aliases ?? System.Array.Empty<string>();
            Complements = complements ?? System.Array.Empty<string>();
            DefaultMouthInfluence = defaultMouthInfluence < 0f ? 0f :
                defaultMouthInfluence > 1f ? 1f : defaultMouthInfluence;
            IsNeutral = isNeutral;
            Dimensions = isNeutral ? EmotionDimensions.Neutral : dimensions;
        }
    }
}
