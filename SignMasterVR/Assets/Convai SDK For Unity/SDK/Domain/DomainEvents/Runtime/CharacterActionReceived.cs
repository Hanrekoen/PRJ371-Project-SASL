using System;
using System.Collections.Generic;
using Convai.Shared.Types;

namespace Convai.Domain.DomainEvents.Runtime
{
    /// <summary>
    ///     Domain event raised when backend returns ordered structured actions for a character turn.
    /// </summary>
    /// <remarks>
    ///     Subscribe via EventHub or <c>ConvaiManager.Events.OnCharacterActionReceived</c>.
    ///     <code>
    /// _eventHub.Subscribe&lt;CharacterActionReceived&gt;(this, e =&gt;
    /// {
    ///     foreach (ConvaiActionCommand action in e.Actions)
    ///     {
    ///         Debug.Log($"Character {e.CharacterId} action command: {action.Name} (target={action.Target})");
    ///         PlayAnimation(e.CharacterId, action.Name);
    ///     }
    /// });
    /// </code>
    /// </remarks>
    public readonly struct CharacterActionReceived
    {
        /// <summary>The character's unique identifier (resolved from participant ID when possible).</summary>
        public string CharacterId { get; }

        /// <summary>Ordered backend action commands for this turn. May be empty for an explicit no-op batch.</summary>
        public IReadOnlyList<ConvaiActionCommand> Actions { get; }

        /// <summary>When the event occurred (UTC).</summary>
        public DateTime Timestamp { get; }

        /// <summary>
        ///     Builds the event, snapshotting <paramref name="actions" /> so later caller
        ///     mutations never leak into subscribers.
        /// </summary>
        public CharacterActionReceived(
            string characterId,
            IReadOnlyList<ConvaiActionCommand> actions,
            DateTime timestamp)
        {
            CharacterId = characterId ?? string.Empty;
            // Snapshot: this ctor is public API and the caller may keep mutating its list.
            Actions = ConvaiActionCommand.CloneBatch(actions);
            Timestamp = timestamp;
        }

        /// <summary>Creates a CharacterActionReceived event with the current UTC timestamp.</summary>
        public static CharacterActionReceived Create(string characterId, IReadOnlyList<ConvaiActionCommand> actions) =>
            new(characterId, actions, DateTime.UtcNow);
    }
}
