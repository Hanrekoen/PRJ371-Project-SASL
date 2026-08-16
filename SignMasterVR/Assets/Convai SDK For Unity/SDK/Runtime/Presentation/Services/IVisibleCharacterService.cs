using System;
using System.Collections.Generic;

namespace Convai.Runtime.Presentation.Services
{
    /// <summary>
    ///     Service for tracking which character IDs are currently visible to the player.
    ///     Used by transcript UIs for filtering and fading logic.
    /// </summary>
    /// <remarks>
    ///     Provides a centralized view of which character IDs are currently visible.
    /// </remarks>
    public interface IVisibleCharacterService
    {
        /// <summary>
        ///     Read-only list of currently visible character IDs.
        /// </summary>
        public IReadOnlyList<string> VisibleCharacterIds { get; }

        /// <summary>
        ///     Gets the number of currently visible characters.
        /// </summary>
        public int Count { get; }

        /// <summary>
        ///     Event fired when a character's visibility changes.
        ///     Parameters: (characterId, isNowVisible)
        /// </summary>
        public event Action<string, bool> VisibleCharacterChanged;

        /// <summary>
        ///     Adds a character to the visible list.
        /// </summary>
        /// <param name="characterId">The character ID to add.</param>
        public void AddCharacter(string characterId);

        /// <summary>
        ///     Removes a character from the visible list.
        /// </summary>
        /// <param name="characterId">The character ID to remove.</param>
        public void RemoveCharacter(string characterId);

        /// <summary>
        ///     Removes all visible characters.
        /// </summary>
        public void RemoveAll();

        /// <summary>
        ///     Removes character at specified index.
        /// </summary>
        /// <param name="index">The index of the character to remove.</param>
        public void RemoveAt(int index);

        /// <summary>
        ///     Checks if a character is currently visible.
        /// </summary>
        /// <param name="characterId">The character ID to check.</param>
        /// <returns>True if the character is visible, false otherwise.</returns>
        public bool Contains(string characterId);

        /// <summary>
        ///     Gets the character at index 0 (for single-Character filters).
        /// </summary>
        /// <returns>The first visible character ID, or null if none.</returns>
        public string GetFirst();
    }
}
