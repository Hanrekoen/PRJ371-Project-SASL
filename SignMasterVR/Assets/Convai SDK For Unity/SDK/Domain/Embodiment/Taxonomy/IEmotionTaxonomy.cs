using System.Collections.Generic;

namespace Convai.Domain.Embodiment.Taxonomy
{
    /// <summary>
    ///     Data-driven emotion vocabulary used by the emotion pipeline.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Replaces the former hard-coded <c>ConvaiEmotionType</c> enum. Users may supply
    ///         any taxonomy (Plutchik, OCC, custom) by authoring an
    ///         <c>EmotionTaxonomyAsset</c>. Server-side emotion labels are resolved through
    ///         <see cref="TryResolve" /> so the taxonomy can evolve independently of the
    ///         runtime protocol.
    ///     </para>
    ///     <para>
    ///         Implementations MUST be immutable after construction and thread-safe for read
    ///         access — the event hub may dispatch <c>CharacterEmotionChanged</c> from any
    ///         thread, and the pipeline resolves labels eagerly.
    ///     </para>
    /// </remarks>
    public interface IEmotionTaxonomy
    {
        /// <summary>All descriptors in the taxonomy, in stable authoring order.</summary>
        IReadOnlyList<EmotionDescriptor> Emotions { get; }

        /// <summary>The single neutral descriptor (never null in a valid taxonomy).</summary>
        EmotionDescriptor Neutral { get; }

        /// <summary>
        ///     Resolves a server-supplied label to the canonical descriptor using the label
        ///     table and aliases. Returns <c>false</c> when the label is unknown; callers
        ///     should fall back to <see cref="Neutral" />.
        /// </summary>
        bool TryResolve(string serverLabel, out EmotionDescriptor descriptor);
    }
}
