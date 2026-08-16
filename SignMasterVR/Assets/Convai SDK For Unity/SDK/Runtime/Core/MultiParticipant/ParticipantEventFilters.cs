using System;
using System.Collections.Generic;

namespace Convai.Runtime.Core.MultiParticipant
{
    /// <summary>
    ///     Provides filter predicates for participant-scoped events.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         These filters enable event subscription to be scoped to specific participants
    ///         in multi-participant scenarios.
    ///     </para>
    ///     <para>
    ///         <b>Usage</b>:
    ///         <code>
    ///         eventHub.Subscribe&lt;TranscriptChunk&gt;(
    ///             handler,
    ///             ParticipantEventFilters.LocalOnly());
    ///         </code>
    ///     </para>
    /// </remarks>
    public static class ParticipantEventFilters
    {
        /// <summary>
        ///     Creates a filter that only accepts events from local participants.
        /// </summary>
        /// <typeparam name="T">Event type implementing <see cref="IParticipantScopedEvent" />.</typeparam>
        /// <returns>A predicate that returns true for local events.</returns>
        public static Func<T, bool> LocalOnly<T>() where T : IParticipantScopedEvent => evt => evt.IsLocalSource;

        /// <summary>
        ///     Creates a filter that only accepts events from remote participants.
        /// </summary>
        /// <typeparam name="T">Event type implementing <see cref="IParticipantScopedEvent" />.</typeparam>
        /// <returns>A predicate that returns true for remote events.</returns>
        public static Func<T, bool> RemoteOnly<T>() where T : IParticipantScopedEvent => evt => !evt.IsLocalSource;

        /// <summary>
        ///     Creates a filter that only accepts events from a specific player.
        /// </summary>
        /// <typeparam name="T">Event type implementing <see cref="IParticipantScopedEvent" />.</typeparam>
        /// <param name="playerId">Player ID to filter for.</param>
        /// <returns>A predicate that returns true for events from the specified player or their characters.</returns>
        public static Func<T, bool> FromPlayer<T>(string playerId) where T : IParticipantScopedEvent
        {
            if (string.IsNullOrEmpty(playerId))
                throw new ArgumentNullException(nameof(playerId));

            return evt => evt.SourceOwnerId == playerId;
        }

        /// <summary>
        ///     Creates a filter that only accepts events from a specific character.
        /// </summary>
        /// <typeparam name="T">Event type implementing <see cref="IParticipantScopedEvent" />.</typeparam>
        /// <param name="characterId">Character ID to filter for.</param>
        /// <returns>A predicate that returns true for events from the specified character.</returns>
        public static Func<T, bool> FromCharacter<T>(string characterId) where T : IParticipantScopedEvent
        {
            if (string.IsNullOrEmpty(characterId))
                throw new ArgumentNullException(nameof(characterId));

            string participantId = $"character:{characterId}";
            return evt => evt.SourceParticipantId == participantId;
        }

        /// <summary>
        ///     Creates a filter that only accepts events from specific participants.
        /// </summary>
        /// <typeparam name="T">Event type implementing <see cref="IParticipantScopedEvent" />.</typeparam>
        /// <param name="participantIds">Set of participant IDs to filter for.</param>
        /// <returns>A predicate that returns true for events from any of the specified participants.</returns>
        public static Func<T, bool> FromParticipants<T>(params string[] participantIds)
            where T : IParticipantScopedEvent
        {
            if (participantIds == null || participantIds.Length == 0)
                throw new ArgumentException("At least one participant ID required.", nameof(participantIds));

            var idSet = new HashSet<string>(participantIds);
            return evt => idSet.Contains(evt.SourceParticipantId);
        }

        /// <summary>
        ///     Combines two filters with AND logic.
        /// </summary>
        /// <typeparam name="T">Event type.</typeparam>
        /// <param name="filter1">First filter.</param>
        /// <param name="filter2">Second filter.</param>
        /// <returns>A predicate that returns true only if both filters return true.</returns>
        public static Func<T, bool> And<T>(Func<T, bool> filter1, Func<T, bool> filter2)
        {
            if (filter1 == null) throw new ArgumentNullException(nameof(filter1));
            if (filter2 == null) throw new ArgumentNullException(nameof(filter2));

            return evt => filter1(evt) && filter2(evt);
        }

        /// <summary>
        ///     Combines two filters with OR logic.
        /// </summary>
        /// <typeparam name="T">Event type.</typeparam>
        /// <param name="filter1">First filter.</param>
        /// <param name="filter2">Second filter.</param>
        /// <returns>A predicate that returns true if either filter returns true.</returns>
        public static Func<T, bool> Or<T>(Func<T, bool> filter1, Func<T, bool> filter2)
        {
            if (filter1 == null) throw new ArgumentNullException(nameof(filter1));
            if (filter2 == null) throw new ArgumentNullException(nameof(filter2));

            return evt => filter1(evt) || filter2(evt);
        }

        /// <summary>
        ///     Inverts a filter.
        /// </summary>
        /// <typeparam name="T">Event type.</typeparam>
        /// <param name="filter">Filter to invert.</param>
        /// <returns>A predicate that returns the opposite of the original filter.</returns>
        public static Func<T, bool> Not<T>(Func<T, bool> filter)
        {
            if (filter == null) throw new ArgumentNullException(nameof(filter));

            return evt => !filter(evt);
        }
    }
}
