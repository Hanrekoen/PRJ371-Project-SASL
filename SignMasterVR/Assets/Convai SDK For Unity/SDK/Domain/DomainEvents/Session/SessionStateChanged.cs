using System;

namespace Convai.Domain.DomainEvents.Session
{
    /// <summary>
    ///     Domain event raised when a session transitions from one state to another.
    /// </summary>
    /// <remarks>
    ///     Published whenever session state changes so subscribers can react to
    ///     connection lifecycle updates.
    /// </remarks>
    public readonly struct SessionStateChanged
    {
        /// <summary>
        ///     The previous session state.
        /// </summary>
        public SessionState OldState { get; }

        /// <summary>
        ///     The new session state.
        /// </summary>
        public SessionState NewState { get; }

        /// <summary>
        ///     The session identifier (can be null if not yet assigned).
        /// </summary>
        public string SessionId { get; }

        /// <summary>
        ///     When the state change occurred (UTC).
        /// </summary>
        public DateTime Timestamp { get; }

        /// <summary>
        ///     Optional error snapshot if transitioning to Error state.
        /// </summary>
        public SessionError? Error { get; }

        /// <summary>
        ///     Optional error code if transitioning to Error state.
        /// </summary>
        public string ErrorCode => Error?.ErrorCode;

        /// <summary>
        ///     Creates a new SessionStateChanged event.
        /// </summary>
        public SessionStateChanged(
            SessionState oldState,
            SessionState newState,
            string sessionId,
            DateTime timestamp,
            SessionError? error = null)
        {
            OldState = oldState;
            NewState = newState;
            SessionId = sessionId;
            Timestamp = timestamp;
            Error = error;
        }

        /// <summary>
        ///     Creates a SessionStateChanged event with the current UTC timestamp.
        /// </summary>
        /// <param name="oldState">The previous session state</param>
        /// <param name="newState">The new session state</param>
        /// <param name="sessionId">The session identifier</param>
        /// <param name="errorCode">Optional error code if transitioning to Error state</param>
        /// <returns>A new SessionStateChanged event</returns>
        public static SessionStateChanged Create(
            SessionState oldState,
            SessionState newState,
            string sessionId,
            SessionError? error = null)
        {
            return new SessionStateChanged(
                oldState,
                newState,
                sessionId,
                DateTime.UtcNow,
                error
            );
        }

        /// <summary>
        ///     Checks if this state change represents a successful connection.
        /// </summary>
        public bool IsConnectionEstablished =>
            OldState == SessionState.Connecting && NewState == SessionState.Connected;

        /// <summary>
        ///     Checks if this state change represents a successful reconnection.
        /// </summary>
        public bool IsReconnectionSuccessful =>
            OldState == SessionState.Reconnecting && NewState == SessionState.Connected;

        /// <summary>
        ///     Checks if this state change represents a disconnection.
        /// </summary>
        public bool IsDisconnected =>
            NewState == SessionState.Disconnected;

        /// <summary>
        ///     Checks if this state change represents an error.
        /// </summary>
        public bool IsError =>
            NewState == SessionState.Error;

        /// <summary>
        ///     Checks if this state change represents entering a reconnecting state.
        /// </summary>
        public bool IsReconnecting =>
            OldState == SessionState.Connected && NewState == SessionState.Reconnecting;
    }
}
