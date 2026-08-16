namespace Convai.Domain.Narrative
{
    /// <summary>
    ///     Data class for syncing section information from backend.
    /// </summary>
    public class SectionSyncData
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="SectionSyncData" /> class.
        /// </summary>
        public SectionSyncData() { }

        /// <summary>
        ///     Initializes a new instance of the <see cref="SectionSyncData" /> class.
        /// </summary>
        /// <param name="sectionId">Section identifier.</param>
        /// <param name="sectionName">Section name.</param>
        public SectionSyncData(string sectionId, string sectionName)
        {
            SectionId = sectionId;
            SectionName = sectionName;
        }

        /// <summary>The section ID.</summary>
        public string SectionId { get; set; }

        /// <summary>The section name.</summary>
        public string SectionName { get; set; }
    }
}
