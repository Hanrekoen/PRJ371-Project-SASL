using System;

namespace Convai.Domain.Narrative
{
    /// <summary>
    ///     Represents a narrative section with entry/exit events.
    ///     Engine-agnostic: uses C# events instead of Unity events.
    /// </summary>
    public class NarrativeSection
    {
        /// <summary>
        ///     Creates a new NarrativeSection.
        /// </summary>
        public NarrativeSection()
        {
            SectionID = string.Empty;
            SectionName = string.Empty;
            IsOrphaned = false;
        }

        /// <summary>
        ///     Creates a new NarrativeSection with the specified ID and name.
        /// </summary>
        /// <param name="sectionId">The unique section identifier.</param>
        /// <param name="sectionName">The display name of the section.</param>
        public NarrativeSection(string sectionId, string sectionName)
        {
            SectionID = sectionId ?? string.Empty;
            SectionName = sectionName ?? string.Empty;
            IsOrphaned = false;
        }

        /// <summary>Unique identifier matching the section ID from Convai's Narrative Design.</summary>
        public string SectionID { get; private set; }

        /// <summary>Display name of the section.</summary>
        public string SectionName { get; private set; }

        /// <summary>Whether this section was deleted on the backend.</summary>
        public bool IsOrphaned { get; private set; }

        /// <summary>Event invoked when this section becomes active.</summary>
        public event Action OnSectionStarted;

        /// <summary>Event invoked when leaving this section.</summary>
        public event Action OnSectionEnded;

        /// <summary>
        ///     Updates the section name (called during sync with backend).
        /// </summary>
        /// <param name="newName">The new section name.</param>
        public void UpdateName(string newName) => SectionName = newName ?? string.Empty;

        /// <summary>
        ///     Sets the orphaned state (called during sync when section is deleted on backend).
        /// </summary>
        /// <param name="isOrphaned">Whether this section is orphaned.</param>
        public void SetOrphaned(bool isOrphaned) => IsOrphaned = isOrphaned;

        /// <summary>
        ///     Sets the section ID (for internal use during sync).
        /// </summary>
        /// <param name="sectionId">The section ID.</param>
        internal void SetSectionId(string sectionId) => SectionID = sectionId ?? string.Empty;

        /// <summary>
        ///     Invokes the section start event.
        /// </summary>
        internal void InvokeStart() => OnSectionStarted?.Invoke();

        /// <summary>
        ///     Invokes the section end event.
        /// </summary>
        internal void InvokeEnd() => OnSectionEnded?.Invoke();
    }
}
