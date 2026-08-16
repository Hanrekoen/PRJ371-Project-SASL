namespace Convai.Domain.Narrative
{
    /// <summary>
    ///     Result of a section sync operation.
    /// </summary>
    public struct SectionSyncResult
    {
        /// <summary>Whether the sync was successful.</summary>
        public bool Success { get; set; }

        /// <summary>Error message if sync failed.</summary>
        public string Error { get; set; }

        /// <summary>Number of sections with updated names.</summary>
        public int SectionsUpdated { get; set; }

        /// <summary>Number of new sections added.</summary>
        public int SectionsAdded { get; set; }

        /// <summary>Number of sections marked as orphaned.</summary>
        public int SectionsOrphaned { get; set; }

        /// <summary>Number of orphaned sections reactivated.</summary>
        public int SectionsReactivated { get; set; }

        /// <summary>
        ///     Gets a summary string of the sync result.
        /// </summary>
        public override string ToString()
        {
            if (!Success) return $"Sync failed: {Error}";

            return
                $"Sync complete: {SectionsAdded} added, {SectionsUpdated} updated, {SectionsOrphaned} orphaned, {SectionsReactivated} reactivated";
        }
    }
}
