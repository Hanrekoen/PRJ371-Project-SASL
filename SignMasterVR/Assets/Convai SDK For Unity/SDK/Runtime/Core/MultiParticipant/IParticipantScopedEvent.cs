namespace Convai.Runtime.Core.MultiParticipant
{
    /// <summary>
    ///     Marker interface for events that can be scoped to a specific participant.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Events implementing this interface include source participant information
    ///         for filtering in multi-participant scenarios.
    ///     </para>
    ///     <para>
    ///         <b>Usage</b>: Use with <see cref="ParticipantEventFilters" /> to filter
    ///         events by local-only, specific player, or specific character.
    ///     </para>
    ///     <para>
    ///         <b>Examples</b>:
    ///         <list type="bullet">
    ///             <item>TranscriptChunk - filter by which character spoke</item>
    ///             <item>AudioPlaybackState - filter by which character's audio</item>
    ///             <item>ActionEvent - filter by which character performed the action</item>
    ///         </list>
    ///     </para>
    /// </remarks>
    public interface IParticipantScopedEvent
    {
        /// <summary>
        ///     The participant ID that is the source of this event.
        /// </summary>
        /// <remarks>
        ///     For character events, this is typically "character:{characterId}".
        ///     For player events, this is the player's participant ID.
        /// </remarks>
        public string SourceParticipantId { get; }

        /// <summary>
        ///     The owner ID (player ID) responsible for this event source.
        /// </summary>
        /// <remarks>
        ///     For character events, this is the player who owns the character.
        ///     For player events, this is the same as the player's ID.
        /// </remarks>
        public string SourceOwnerId { get; }

        /// <summary>
        ///     Whether the event source is local (from the local player or their characters).
        /// </summary>
        public bool IsLocalSource { get; }
    }
}
