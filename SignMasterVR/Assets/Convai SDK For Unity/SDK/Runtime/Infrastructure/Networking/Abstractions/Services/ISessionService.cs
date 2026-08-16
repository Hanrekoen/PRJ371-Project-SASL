using Convai.Infrastructure.Networking.Models;

namespace Convai.Infrastructure.Networking.Services
{
    /// <summary>
    ///     High-level service for managing character session lifecycle.
    ///     Wraps ISessionPersistence with additional business logic for session resume decisions.
    /// </summary>
    /// <remarks>
    ///     This service provides:
    ///     - Session persistence (load/save/clear)
    ///     - Policy-based resume decision making
    ///     - Active session tracking
    ///     Thread-safe: All methods are safe to call from any thread.
    /// </remarks>
    public interface ISessionService
    {
        /// <summary>
        ///     Gets the currently active session ID (null if not in a session).
        /// </summary>
        public string ActiveSessionId { get; }

        /// <summary>
        ///     Gets the currently connected character ID (null if not connected).
        /// </summary>
        public string ActiveCharacterId { get; }

        /// <summary>
        ///     Loads a stored session ID for the specified character.
        /// </summary>
        /// <param name="characterId">The character ID</param>
        /// <returns>The stored session ID, or null if none exists</returns>
        public string LoadStoredSession(string characterId);

        /// <summary>
        ///     Saves a session ID for the specified character.
        /// </summary>
        /// <param name="characterId">The character ID</param>
        /// <param name="sessionId">The session ID to store</param>
        public void StoreSession(string characterId, string sessionId);

        /// <summary>
        ///     Clears any stored session for the specified character.
        /// </summary>
        /// <param name="characterId">The character ID</param>
        public void ClearStoredSession(string characterId);

        /// <summary>
        ///     Clears all stored sessions.
        /// </summary>
        public void ClearAllStoredSessions();

        /// <summary>
        ///     Checks if a stored session exists for the specified character.
        /// </summary>
        /// <param name="characterId">The character ID</param>
        /// <returns>True if a session is stored</returns>
        public bool HasStoredSession(string characterId);

        /// <summary>
        ///     Determines whether to attempt session resume based on policy and stored data.
        /// </summary>
        /// <param name="characterId">The character ID</param>
        /// <param name="policy">The resume policy to apply</param>
        /// <returns>True if resume should be attempted</returns>
        public bool ShouldResumeSession(string characterId, ResumePolicy policy);

        /// <summary>
        ///     Gets the session ID to use for connection based on policy.
        /// </summary>
        /// <param name="characterId">The character ID</param>
        /// <param name="policy">The resume policy to apply</param>
        /// <param name="explicitSessionId">An explicitly provided session ID (takes precedence)</param>
        /// <returns>The session ID to use, or null to start fresh</returns>
        public string GetSessionIdForConnection(string characterId, ResumePolicy policy,
            string explicitSessionId = null);

        /// <summary>
        ///     Sets the active session (called after successful connection).
        /// </summary>
        /// <param name="characterId">The connected character ID</param>
        /// <param name="sessionId">The active session ID</param>
        public void SetActiveSession(string characterId, string sessionId);

        /// <summary>
        ///     Clears the active session (called after disconnect).
        /// </summary>
        public void ClearActiveSession();
    }
}
