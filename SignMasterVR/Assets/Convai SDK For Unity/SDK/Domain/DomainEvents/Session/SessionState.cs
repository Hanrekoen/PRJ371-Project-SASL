namespace Convai.Domain.DomainEvents.Session
{
    /// <summary>
    ///     Represents the current state of a Convai session.
    ///     Standardizes session lifecycle tracking across the SDK.
    /// </summary>
    /// <remarks>
    ///     Observe <see cref="SessionStateChanged" /> to react to session lifecycle updates.
    ///     Typical flows include <c>Disconnected → Connecting → Connected</c> and
    ///     <c>Connected → Disconnecting → Disconnected</c>, with <c>Reconnecting</c>
    ///     used during recovery.
    /// </remarks>
    public enum SessionState
    {
        /// <summary>
        ///     No active session. Initial state and final state after clean disconnect.
        /// </summary>
        Disconnected = 0,

        /// <summary>
        ///     Attempting to establish a new session connection.
        ///     Transitioning from Disconnected to Connected.
        /// </summary>
        Connecting = 1,

        /// <summary>
        ///     Session is active and operational. Can send/receive data.
        /// </summary>
        Connected = 2,

        /// <summary>
        ///     Connection was lost, attempting to reconnect automatically.
        ///     Transitioning from Connected back to Connected (or Error).
        /// </summary>
        Reconnecting = 3,

        /// <summary>
        ///     Gracefully closing the session.
        ///     Transitioning from Connected to Disconnected.
        /// </summary>
        Disconnecting = 4,

        /// <summary>
        ///     Session encountered an unrecoverable error.
        ///     Requires manual intervention to recover.
        /// </summary>
        Error = 5
    }

    /// <summary>
    ///     Extension methods for SessionState enum.
    /// </summary>
    public static class SessionStateExtensions
    {
        /// <summary>
        ///     Checks if the session is in a connected state (Connected or Reconnecting).
        /// </summary>
        /// <param name="state">The session state to check</param>
        /// <returns>True if connected or reconnecting, false otherwise</returns>
        public static bool IsConnected(this SessionState state) =>
            state == SessionState.Connected || state == SessionState.Reconnecting;

        /// <summary>
        ///     Checks if the session is in a transitional state (Connecting, Reconnecting, Disconnecting).
        /// </summary>
        /// <param name="state">The session state to check</param>
        /// <returns>True if in transition, false otherwise</returns>
        public static bool IsTransitioning(this SessionState state)
        {
            return state == SessionState.Connecting
                   || state == SessionState.Reconnecting
                   || state == SessionState.Disconnecting;
        }

        /// <summary>
        ///     Checks if the session is in a stable state (Disconnected, Connected, Error).
        /// </summary>
        /// <param name="state">The session state to check</param>
        /// <returns>True if stable, false otherwise</returns>
        public static bool IsStable(this SessionState state)
        {
            return state == SessionState.Disconnected
                   || state == SessionState.Connected
                   || state == SessionState.Error;
        }

        /// <summary>
        ///     Checks if the session can accept user input/commands.
        /// </summary>
        /// <param name="state">The session state to check</param>
        /// <returns>True if can accept input, false otherwise</returns>
        public static bool CanAcceptInput(this SessionState state) => state == SessionState.Connected;
    }
}
