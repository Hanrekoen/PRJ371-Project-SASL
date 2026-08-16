namespace Convai.Modules.LipSync
{
    /// <summary>
    ///     Match strictness for auto-detecting source blendshapes against mesh blendshape names.
    /// </summary>
    public enum BlendshapeMatchMode
    {
        /// <summary>
        ///     Require exact (case-insensitive) name equality.
        /// </summary>
        Exact = 0,

        /// <summary>
        ///     Allow contains-based matching after exact matching fails.
        /// </summary>
        Contains = 1,

        /// <summary>
        ///     Allow normalized-name fallback (prefix cleanup) after contains matching.
        /// </summary>
        Fuzzy = 2
    }
}
