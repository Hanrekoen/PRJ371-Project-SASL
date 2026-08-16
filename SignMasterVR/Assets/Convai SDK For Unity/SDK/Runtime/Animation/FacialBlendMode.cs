namespace Convai.Runtime.Animation
{
    /// <summary>
    ///     Determines how multiple layer contributions are combined within a single facial region.
    /// </summary>
    public enum FacialBlendMode
    {
        /// <summary>
        ///     Each layer's value is multiplied by its interpolated weight and summed, then clamped to 0-100.
        ///     Preserves subtle contributions from multiple sources (recommended default).
        /// </summary>
        WeightedAdditive = 0,

        /// <summary>
        ///     Each layer's value is multiplied by its interpolated weight; the largest result wins.
        ///     Useful for rigs that prefer a single dominant expression source.
        /// </summary>
        Max = 1,

        /// <summary>
        ///     The highest-priority non-zero layer value (scaled by its weight) takes full control.
        ///     Priority order: LipSync > Emotion > Custom.
        /// </summary>
        Override = 2
    }
}
