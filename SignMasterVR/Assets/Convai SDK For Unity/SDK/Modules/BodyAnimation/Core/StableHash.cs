namespace Convai.Modules.BodyAnimation.Core
{
    /// <summary>
    ///     A stable, session-independent string hash. <see cref="string.GetHashCode()" /> carries
    ///     no cross-version/cross-session stability contract (and randomizes per process on some
    ///     runtimes), which is exactly wrong for seeding reproducible behavior — the same
    ///     character identity must hash to the same value every run so a reported bug can be
    ///     replayed. FNV-1a is simple, allocation-free, and has no such guarantee to break.
    /// </summary>
    internal static class StableHash
    {
        private const int FnvOffsetBasis = unchecked((int)2166136261);
        private const int FnvPrime = 16777619;

        /// <summary>Fixed value returned for a null or empty input so callers never need a branch.</summary>
        private const int EmptyValue = FnvOffsetBasis;

        /// <summary>FNV-1a 32-bit hash of <paramref name="value" />, stable across runs and machines.</summary>
        internal static int Of(string value)
        {
            if (string.IsNullOrEmpty(value)) return EmptyValue;

            unchecked
            {
                int hash = FnvOffsetBasis;
                for (int i = 0; i < value.Length; i++)
                {
                    hash ^= value[i];
                    hash *= FnvPrime;
                }
                return hash;
            }
        }
    }
}
