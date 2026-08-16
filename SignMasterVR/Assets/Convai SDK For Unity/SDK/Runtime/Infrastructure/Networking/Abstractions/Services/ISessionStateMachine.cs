using Convai.Domain.DomainEvents.Session;

namespace Convai.Infrastructure.Networking.Services
{
    /// <summary>
    ///     State machine for managing session lifecycle with validated transitions and event publishing.
    ///     Centralizes state management that was previously scattered across ConvaiRoomManager.
    /// </summary>
    /// <remarks>
    ///     This interface provides:
    ///     - Thread-safe state access
    ///     - Validated state transitions (only valid transitions are allowed)
    ///     - Automatic SessionStateChanged event publishing via EventHub
    ///     - Session ID tracking
    ///     State Transitions (as defined in SessionState enum):
    ///     - Disconnected → Connecting
    ///     - Connecting → Connected | Error
    ///     - Connected → Disconnecting | Reconnecting
    ///     - Reconnecting → Connected | Error
    ///     - Disconnecting → Disconnected
    ///     - Error → Disconnected
    /// </remarks>
    public interface ISessionStateMachine
    {
        /// <summary>
        ///     Gets the current session state.
        ///     Thread-safe property.
        /// </summary>
        public SessionState CurrentState { get; }

        /// <summary>
        ///     Gets the current session ID (null if not connected).
        ///     Thread-safe property.
        /// </summary>
        public string SessionId { get; }

        /// <summary>
        ///     Attempts to transition to a new state.
        ///     Returns true if the transition was valid and completed.
        /// </summary>
        /// <param name="newState">The target state</param>
        /// <param name="sessionId">Optional session ID (for Connected state)</param>
        /// <param name="error">Optional error snapshot (for Error state)</param>
        /// <returns>True if transition succeeded; false if transition was invalid</returns>
        public bool TryTransition(SessionState newState, string sessionId = null, SessionError? error = null);

        /// <summary>
        ///     Forces a transition to a new state, bypassing validation.
        ///     Use with caution; primarily for error recovery scenarios.
        /// </summary>
        /// <param name="newState">The target state</param>
        /// <param name="sessionId">Optional session ID</param>
        /// <param name="error">Optional error snapshot</param>
        public void ForceTransition(SessionState newState, string sessionId = null, SessionError? error = null);

        /// <summary>
        ///     Resets the state machine to Disconnected state.
        ///     Clears session ID and any error state.
        /// </summary>
        public void Reset();

        /// <summary>
        ///     Checks if a transition from the current state to the target state is valid.
        /// </summary>
        /// <param name="targetState">The target state to check</param>
        /// <returns>True if the transition is valid</returns>
        public bool CanTransitionTo(SessionState targetState);
    }
}
