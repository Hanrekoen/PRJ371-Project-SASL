namespace Convai.Infrastructure.Networking
{
    /// <summary>
    ///     Registry for managing remote player audio sources in multiplayer Convai sessions.
    ///     This interface provides a future-ready abstraction for multiplayer audio routing.
    /// </summary>
    /// <remarks>
    ///     In single-player mode, this registry will typically be empty.
    ///     In multiplayer mode, it tracks audio sources for remote human players
    ///     (not Characters, which are managed by <c>IAgentRegistry</c>).
    /// </remarks>
    internal interface IRemotePlayerRegistry
    {
        /// <summary>
        ///     Gets the count of registered remote players.
        /// </summary>
        public int PlayerCount { get; }

        /// <summary>
        ///     Registers a remote player with their associated audio output.
        /// </summary>
        /// <param name="participantId">The LiveKit participant identifier.</param>
        /// <param name="displayName">The display name of the remote player.</param>
        public void RegisterPlayer(string participantId, string displayName);

        /// <summary>
        ///     Unregisters a remote player from the registry.
        /// </summary>
        /// <param name="participantId">The LiveKit participant identifier.</param>
        public void UnregisterPlayer(string participantId);

        /// <summary>
        ///     Sets the local mute state for a remote player's audio.
        /// </summary>
        /// <param name="participantId">The LiveKit participant identifier.</param>
        /// <param name="muted">True to mute the player's audio locally; false to unmute.</param>
        /// <returns>True if the player was found and mute state was set; otherwise, false.</returns>
        public bool SetPlayerMuted(string participantId, bool muted);

        /// <summary>
        ///     Gets the local mute state for a remote player's audio.
        /// </summary>
        /// <param name="participantId">The LiveKit participant identifier.</param>
        /// <returns>True if the player's audio is muted locally; false otherwise.</returns>
        public bool IsPlayerMuted(string participantId);

        /// <summary>
        ///     Clears all registered remote players.
        /// </summary>
        public void Clear();
    }
}
