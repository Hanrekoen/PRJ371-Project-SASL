namespace Convai.Domain.Abstractions
{
    /// <summary>
    ///     Defines the contract for persisting and retrieving session identifiers.
    ///     This interface is Unity-independent and can be implemented using different
    ///     storage backends (PlayerPrefs, file system, cloud storage, etc.).
    /// </summary>
    /// <remarks>
    ///     This port is defined in Domain so that Application layer can depend on it
    ///     without taking a dependency on Infrastructure. The default implementation is
    ///     <c>KeyValueStoreSessionPersistence</c> in the Infrastructure layer.
    /// </remarks>
    public interface ISessionPersistence
    {
        /// <summary>
        ///     Loads the stored session identifier for the specified character.
        /// </summary>
        /// <param name="characterId">The unique identifier of the character.</param>
        /// <returns>The stored session ID if found; otherwise, null.</returns>
        public string LoadSession(string characterId);

        /// <summary>
        ///     Saves a session identifier for the specified character.
        /// </summary>
        /// <param name="characterId">The unique identifier of the character.</param>
        /// <param name="sessionId">The session identifier to persist.</param>
        public void SaveSession(string characterId, string sessionId);

        /// <summary>
        ///     Clears the stored session identifier for the specified character.
        /// </summary>
        /// <param name="characterId">The unique identifier of the character.</param>
        public void ClearSession(string characterId);

        /// <summary>
        ///     Clears all stored session identifiers.
        /// </summary>
        public void ClearAllSessions();

        /// <summary>
        ///     Checks whether a session exists for the specified character.
        /// </summary>
        /// <param name="characterId">The unique identifier of the character.</param>
        /// <returns>True if a session is stored for the character; otherwise, false.</returns>
        public bool HasSession(string characterId);
    }
}
