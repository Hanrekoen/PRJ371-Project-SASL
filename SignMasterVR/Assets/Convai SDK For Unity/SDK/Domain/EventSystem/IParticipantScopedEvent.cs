namespace Convai.Domain.EventSystem
{
    /// <summary>
    ///     Marker interface for events that are scoped to a specific participant.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         In multi-participant rooms, events may originate from different participants.
    ///         Events implementing this interface can be filtered by participant source,
    ///         allowing handlers to process only events from specific participants.
    ///     </para>
    ///     <para>
    ///         <b>Usage</b>: Implement this interface on event structs that need participant filtering:
    ///     </para>
    ///     <code>
    /// public readonly struct TranscriptChunk : IParticipantScopedEvent
    /// {
    ///     public string ParticipantId { get; }
    ///     public string SourceCharacterId { get; }
    ///     // ... other members
    /// }
    /// </code>
    ///     <para>
    ///         <b>Single-Player Mode</b>: In single-player mode, <see cref="ParticipantId" />
    ///         returns the local player's ID.
    ///     </para>
    /// </remarks>
    public interface IParticipantScopedEvent
    {
        /// <summary>
        ///     The ID of the participant who originated this event.
        /// </summary>
        public string ParticipantId { get; }

        /// <summary>
        ///     The ID of the character associated with this event (if applicable).
        ///     May be null for events not tied to a specific character.
        /// </summary>
        public string SourceCharacterId { get; }
    }
}
