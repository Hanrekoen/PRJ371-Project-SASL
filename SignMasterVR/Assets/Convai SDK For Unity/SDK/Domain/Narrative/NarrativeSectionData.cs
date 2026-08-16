namespace Convai.Domain.Narrative
{
    /// <summary>
    ///     Data structure containing full narrative section information including behavior tree data.
    /// </summary>
    public sealed class NarrativeSectionData
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="NarrativeSectionData" /> class.
        /// </summary>
        public NarrativeSectionData()
        {
            SectionId = string.Empty;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="NarrativeSectionData" /> class.
        /// </summary>
        /// <param name="sectionId">Section identifier.</param>
        /// <param name="btCode">Behavior tree code.</param>
        /// <param name="btConstants">Behavior tree constants as a JSON string.</param>
        public NarrativeSectionData(string sectionId, string btCode = null, string btConstants = null)
        {
            SectionId = sectionId ?? string.Empty;
            BehaviorTreeCode = btCode;
            BehaviorTreeConstants = btConstants;
        }

        /// <summary>Unique identifier for the section.</summary>
        public string SectionId { get; set; }

        /// <summary>Behavior tree code for this section.</summary>
        public string BehaviorTreeCode { get; set; }

        /// <summary>Behavior tree constants as JSON string.</summary>
        public string BehaviorTreeConstants { get; set; }
    }
}
