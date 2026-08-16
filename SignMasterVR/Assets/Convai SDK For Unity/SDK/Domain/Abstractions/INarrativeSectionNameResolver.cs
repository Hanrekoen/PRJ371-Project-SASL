namespace Convai.Domain.Abstractions
{
    /// <summary>
    ///     Provides human-readable section names for narrative section IDs.
    ///     Used for enhanced logging and debugging of narrative section changes.
    /// </summary>
    public interface INarrativeSectionNameResolver
    {
        /// <summary>
        ///     Attempts to resolve a human-readable section name from a section ID.
        /// </summary>
        /// <param name="sectionId">The unique section identifier (GUID).</param>
        /// <param name="sectionName">The human-readable section name, or null if not found.</param>
        /// <returns>True if the section name was found; false otherwise.</returns>
        public bool TryGetSectionName(string sectionId, out string sectionName);
    }
}
