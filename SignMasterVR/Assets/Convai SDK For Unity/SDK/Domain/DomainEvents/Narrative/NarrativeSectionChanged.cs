using System;

namespace Convai.Domain.DomainEvents.Narrative
{
    /// <summary>
    ///     Domain event raised when the narrative design section changes.
    ///     Surfaces behavior-tree-response RTVI message via EventHub.
    /// </summary>
    /// <remarks>
    ///     This event is published via EventHub whenever a behavior-tree-response message is received
    ///     from the server. It includes the full section data including behavior tree code and constants.
    ///     Typically use EventDeliveryPolicy.MainThread for Unity component updates and
    ///     EventDeliveryPolicy.Immediate for lightweight logging or analytics.
    /// </remarks>
    public readonly struct NarrativeSectionChanged
    {
        /// <summary>
        ///     The unique identifier of the narrative section.
        /// </summary>
        public string SectionId { get; }

        /// <summary>
        ///     The Convai character ID this section belongs to.
        /// </summary>
        public string CharacterId { get; }

        /// <summary>
        ///     The transport-layer participant ID.
        /// </summary>
        public string ParticipantId { get; }

        /// <summary>
        ///     Behavior tree code for this section (may be null or empty).
        /// </summary>
        public string BehaviorTreeCode { get; }

        /// <summary>
        ///     Behavior tree constants as JSON string (may be null or empty).
        /// </summary>
        public string BehaviorTreeConstants { get; }

        /// <summary>
        ///     When the section change was received (UTC).
        /// </summary>
        public DateTime Timestamp { get; }

        /// <summary>
        ///     Creates a new NarrativeSectionChanged event.
        /// </summary>
        public NarrativeSectionChanged(
            string sectionId,
            string characterId,
            string participantId,
            string behaviorTreeCode,
            string behaviorTreeConstants,
            DateTime timestamp)
        {
            SectionId = sectionId ?? string.Empty;
            CharacterId = characterId ?? string.Empty;
            ParticipantId = participantId ?? string.Empty;
            BehaviorTreeCode = behaviorTreeCode ?? string.Empty;
            BehaviorTreeConstants = behaviorTreeConstants ?? string.Empty;
            Timestamp = timestamp;
        }

        /// <summary>
        ///     Creates a NarrativeSectionChanged event with the current UTC timestamp.
        /// </summary>
        /// <param name="sectionId">The narrative section ID</param>
        /// <param name="characterId">The Convai character ID</param>
        /// <param name="participantId">The transport-layer participant ID</param>
        /// <param name="behaviorTreeCode">The behavior tree code (optional)</param>
        /// <param name="behaviorTreeConstants">The behavior tree constants as JSON (optional)</param>
        /// <returns>A new NarrativeSectionChanged event</returns>
        public static NarrativeSectionChanged Create(
            string sectionId,
            string characterId,
            string participantId,
            string behaviorTreeCode = null,
            string behaviorTreeConstants = null)
        {
            return new NarrativeSectionChanged(
                sectionId,
                characterId,
                participantId,
                behaviorTreeCode,
                behaviorTreeConstants,
                DateTime.UtcNow);
        }
    }
}
