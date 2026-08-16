using System;
using System.Collections.Generic;
using Convai.Runtime.Behaviors;

namespace Convai.Runtime.Core.MultiParticipant
{
    /// <summary>
    ///     Controller for managing multiple characters simultaneously.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>1:N Support</b>: Enables one player to interact with multiple characters
    ///         at once, with focus management for single-target and broadcast operations.
    ///     </para>
    ///     <para>
    ///         This interface is scaffolded for future implementation.
    ///         Single-character mode works by treating the single character as the focus.
    ///     </para>
    /// </remarks>
    public interface IMultiCharacterController
    {
        #region Focus Management

        /// <summary>
        ///     The character that currently has focus (receives single-target input).
        /// </summary>
        public IConvaiCharacterAgent FocusedCharacter { get; }

        /// <summary>
        ///     Sets the focused character.
        /// </summary>
        /// <param name="character">Character to focus. Must be active.</param>
        public void SetFocus(IConvaiCharacterAgent character);

        /// <summary>
        ///     Clears the current focus.
        /// </summary>
        public void ClearFocus();

        /// <summary>
        ///     Cycles focus to the next active character.
        /// </summary>
        public void FocusNext();

        /// <summary>
        ///     Cycles focus to the previous active character.
        /// </summary>
        public void FocusPrevious();

        #endregion

        #region Active Characters

        /// <summary>
        ///     All characters currently active for this controller.
        /// </summary>
        public IReadOnlyList<IConvaiCharacterAgent> ActiveCharacters { get; }

        /// <summary>
        ///     Number of active characters.
        /// </summary>
        public int ActiveCount { get; }

        /// <summary>
        ///     Activates a character for multi-character control.
        /// </summary>
        public void Activate(IConvaiCharacterAgent character);

        /// <summary>
        ///     Deactivates a character.
        /// </summary>
        public void Deactivate(IConvaiCharacterAgent character);

        /// <summary>
        ///     Activates multiple characters at once.
        /// </summary>
        public void ActivateAll(IEnumerable<IConvaiCharacterAgent> characters);

        /// <summary>
        ///     Deactivates all characters.
        /// </summary>
        public void DeactivateAll();

        #endregion

        #region Bulk Operations

        /// <summary>
        ///     Sends text input to all active characters (broadcast mode).
        /// </summary>
        /// <param name="text">Text to send.</param>
        public void BroadcastText(string text);

        /// <summary>
        ///     Sends text input to only the focused character.
        /// </summary>
        /// <param name="text">Text to send.</param>
        public void SendTextToFocused(string text);

        /// <summary>
        ///     Interrupts all active characters.
        /// </summary>
        public void InterruptAll();

        #endregion

        #region Events

        /// <summary>
        ///     Fired when the focused character changes.
        /// </summary>
        public event Action<IConvaiCharacterAgent> FocusChanged;

        /// <summary>
        ///     Fired when a character is activated.
        /// </summary>
        public event Action<IConvaiCharacterAgent> CharacterActivated;

        /// <summary>
        ///     Fired when a character is deactivated.
        /// </summary>
        public event Action<IConvaiCharacterAgent> CharacterDeactivated;

        #endregion
    }
}
