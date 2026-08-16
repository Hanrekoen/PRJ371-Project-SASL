using System;
using System.Collections.Generic;
using Convai.Runtime.Behaviors;

namespace Convai.Runtime.Core.Coordinators
{
    /// <summary>
    ///     Coordinates character ownership, activation, and focus within a room session.
    ///     Extracted from ConvaiRoomManager to enable focused testing and clear responsibility boundaries.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This coordinator tracks which player owns which characters, which characters are active
    ///         in the current session, and which character has UI focus.
    ///     </para>
    ///     <para>
    ///         <b>Multi-Participant Design (1:N)</b>: Designed for one player owning multiple characters.
    ///         In multiplayer scenarios, each player can own different subsets of characters.
    ///     </para>
    ///     <para>
    ///         <b>Unity-Native Design</b>: Uses <see cref="IConvaiCharacterAgent" /> and
    ///         <see cref="IConvaiPlayerAgent" /> directly, following the SDK's Unity-native architecture.
    ///     </para>
    /// </remarks>
    public interface IRoomOwnershipCoordinator
    {
        #region Local Player

        /// <summary>
        ///     The local player agent. Null if no local player is registered.
        /// </summary>
        public IConvaiPlayerAgent LocalPlayer { get; }

        #endregion

        #region Lifecycle

        /// <summary>
        ///     Whether there is a pending ownership change that requires reconnection.
        /// </summary>
        public bool HasPendingOwnershipReconnect { get; }

        #endregion

        #region Ownership

        /// <summary>
        ///     All characters owned by the local player (1:N ownership support).
        /// </summary>
        public IReadOnlyList<IConvaiCharacterAgent> OwnedCharacters { get; }

        /// <summary>
        ///     Gets all characters owned by a specific owner ID.
        /// </summary>
        /// <param name="ownerId">The owner identifier used by the room ownership coordinator.</param>
        /// <returns>Read-only list of characters owned by this player.</returns>
        public IReadOnlyList<IConvaiCharacterAgent> GetCharactersByOwner(string ownerId);

        #endregion

        #region Active Characters

        /// <summary>
        ///     Characters that are currently active in the room session.
        ///     Active characters can receive and respond to player input.
        /// </summary>
        public IReadOnlyList<IConvaiCharacterAgent> ActiveCharacters { get; }

        /// <summary>
        ///     Activates a character for the current session.
        /// </summary>
        /// <param name="character">The character to activate.</param>
        public void ActivateCharacter(IConvaiCharacterAgent character);

        /// <summary>
        ///     Deactivates a character from the current session.
        /// </summary>
        /// <param name="character">The character to deactivate.</param>
        public void DeactivateCharacter(IConvaiCharacterAgent character);

        #endregion

        #region Focus

        /// <summary>
        ///     The character that currently has UI focus (attention).
        ///     This is the primary character for single-target interactions.
        /// </summary>
        /// <remarks>
        ///     Focus is separate from activation. A character can be active but not focused.
        ///     Focus typically determines which character receives text input in single-target mode.
        /// </remarks>
        public IConvaiCharacterAgent FocusedCharacter { get; }

        /// <summary>
        ///     Sets the focused character.
        /// </summary>
        /// <param name="character">The character to focus. Must be an active character.</param>
        /// <exception cref="InvalidOperationException">If the character is not active.</exception>
        public void SetFocusedCharacter(IConvaiCharacterAgent character);

        #endregion

        #region Events

        /// <summary>
        ///     Fired when ownership assignments change (character added/removed from ownership).
        /// </summary>
        public event Action OwnershipChanged;

        /// <summary>
        ///     Fired when a character is activated.
        /// </summary>
        public event Action<IConvaiCharacterAgent> CharacterActivated;

        /// <summary>
        ///     Fired when a character is deactivated.
        /// </summary>
        public event Action<IConvaiCharacterAgent> CharacterDeactivated;

        /// <summary>
        ///     Fired when the focused character changes.
        /// </summary>
        public event Action<IConvaiCharacterAgent> FocusChanged;

        #endregion
    }
}
