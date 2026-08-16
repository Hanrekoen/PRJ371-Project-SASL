using UnityEngine;

namespace Convai.Runtime.Adapters.Networking
{
    /// <summary>
    ///     Runtime-only interface for resolving AudioSource instances for characters.
    ///     This keeps the Infrastructure layer Unity-free while allowing Runtime to manage audio routing.
    /// </summary>
    public interface ICharacterAudioSourceResolver
    {
        /// <summary>
        ///     Attempts to get the AudioSource for a given character.
        /// </summary>
        /// <param name="characterId">The character identifier.</param>
        /// <param name="audioSource">The resolved AudioSource, or null if not found.</param>
        /// <returns>True if an AudioSource was found; otherwise, false.</returns>
        public bool TryGetAudioSource(string characterId, out AudioSource audioSource);

        /// <summary>
        ///     Sets the AudioSource for a given character.
        /// </summary>
        /// <param name="characterId">The character identifier.</param>
        /// <param name="audioSource">The AudioSource to associate with the character.</param>
        public void SetAudioSource(string characterId, AudioSource audioSource);
    }
}
