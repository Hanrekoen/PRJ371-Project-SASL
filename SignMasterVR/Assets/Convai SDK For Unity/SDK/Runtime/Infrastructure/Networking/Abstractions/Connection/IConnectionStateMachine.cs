using System;

namespace Convai.Infrastructure.Networking.Connection
{
    /// <summary>Represents the current backend connection state.</summary>
    public enum ConnectionState
    {
        /// <summary>Not connected.</summary>
        Disconnected = 0,

        /// <summary>Connection attempt in progress.</summary>
        Connecting = 1,

        /// <summary>Connected to the backend.</summary>
        Connected = 2,

        /// <summary>Reconnecting after a transient failure.</summary>
        Reconnecting = 3,

        /// <summary>Disconnect in progress.</summary>
        Disconnecting = 4
    }

    /// <summary>
    ///     Interface for managing connection state transitions with validation.
    /// </summary>
    /// <remarks>
    ///     Provides validated state transitions for LiveKit room connections.
    ///     Invalid transitions are rejected to ensure consistent state.
    ///     State Transition Rules:
    ///     - Disconnected -> Connecting
    ///     - Connecting -> Connected | Disconnected (on error)
    ///     - Connected -> Reconnecting | Disconnecting
    ///     - Reconnecting -> Connected | Disconnected
    ///     - Disconnecting -> Disconnected
    ///     Thread-safe: Implementations must be thread-safe.
    /// </remarks>
    public interface IConnectionStateMachine
    {
        /// <summary>
        ///     Gets the current connection state.
        /// </summary>
        public ConnectionState CurrentState { get; }

        /// <summary>
        ///     Raised when the connection state changes.
        ///     Parameters: (oldState, newState, errorMessage)
        /// </summary>
        public event Action<ConnectionState, ConnectionState, string> StateChanged;

        /// <summary>
        ///     Attempts to transition to the specified state.
        ///     Returns false if the transition is invalid from the current state.
        /// </summary>
        /// <param name="newState">The target state.</param>
        /// <param name="errorMessage">Optional error message for error-related transitions.</param>
        /// <returns>True if the transition succeeded; false if invalid.</returns>
        public bool TryTransition(ConnectionState newState, string errorMessage = null);

        /// <summary>
        ///     Forces a transition to the specified state, bypassing validation.
        ///     Use with caution - intended for recovery scenarios.
        /// </summary>
        /// <param name="newState">The target state.</param>
        /// <param name="errorMessage">Optional error message.</param>
        public void ForceTransition(ConnectionState newState, string errorMessage = null);

        /// <summary>
        ///     Checks if a transition to the specified state is valid from the current state.
        /// </summary>
        /// <param name="targetState">The target state to check.</param>
        /// <returns>True if the transition would be valid.</returns>
        public bool CanTransitionTo(ConnectionState targetState);

        /// <summary>
        ///     Resets the state machine to Disconnected state.
        /// </summary>
        public void Reset();
    }
}
