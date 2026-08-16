using System;
using Convai.Domain.Errors;

namespace Convai.Domain.DomainEvents.Session
{
    /// <summary>
    ///     Broad origin of a lifecycle/session error.
    /// </summary>
    public enum SessionErrorStage
    {
        Unknown = 0,
        Configuration = 1,
        ConnectApi = 2,
        Transport = 3,
        SessionRecovery = 4,
        Runtime = 5
    }

    /// <summary>
    ///     Domain event raised when a session encounters an error.
    ///     Standardizes error reporting across the SDK.
    /// </summary>
    /// <remarks>
    ///     This event is published via EventHub whenever a session error occurs.
    ///     It provides structured error information that can be logged, displayed to users,
    ///     or used for diagnostics.
    ///     Error codes follow a hierarchical naming convention.
    ///     Use <see cref="SessionErrorCodes" /> constants as the canonical source:
    ///     - "connection.timeout" - Connection attempt timed out
    ///     - "connection.auth_failed" - Authentication failed
    ///     - "connection.network_error" - Network error during connection
    ///     - "session.token_expired" - Session token expired
    ///     - "session.invalid_state" - Invalid state transition attempted
    ///     - "transport.livekit_error" - LiveKit transport error
    ///     - "protocol.message_invalid" - Invalid protocol message received
    ///     Usage:
    ///     <code>
    /// 
    /// private readonly IEventHub _eventHub;
    /// 
    /// 
    /// _eventHub.Publish(new SessionError(
    ///     errorCode: SessionErrorCodes.ConnectionTimeout,
    ///     message: "Failed to connect to LiveKit room within 30 seconds",
    ///     sessionId: _sessionId,
    ///     timestamp: DateTime.UtcNow,
    ///     isRecoverable: true
    /// ));
    /// 
    /// 
    /// _eventHub.Subscribe&lt;SessionError&gt;(this, e =>
    /// {
    ///     Debug.LogError($"Session error [{e.ErrorCode}]: {e.Message}");
    /// 
    ///     if (!e.IsRecoverable)
    ///     {
    /// 
    ///     }
    /// });
    /// </code>
    /// </remarks>
    public readonly struct SessionError
    {
        /// <summary>
        ///     Hierarchical error code (e.g., "connection.timeout").
        /// </summary>
        public string ErrorCode { get; }

        /// <summary>
        ///     Human-readable error message.
        /// </summary>
        public string Message { get; }

        /// <summary>
        ///     The session identifier (can be null if error occurred before session creation).
        /// </summary>
        public string SessionId { get; }

        /// <summary>
        ///     When the error occurred (UTC).
        /// </summary>
        public DateTime Timestamp { get; }

        /// <summary>
        ///     Whether the error can be recovered from (e.g., via retry).
        /// </summary>
        public bool IsRecoverable { get; }

        /// <summary>
        ///     Optional exception that caused the error.
        /// </summary>
        public Exception Exception { get; }

        /// <summary>
        ///     Broad lifecycle stage where the error originated.
        /// </summary>
        public SessionErrorStage Stage { get; }

        /// <summary>
        ///     Optional HTTP status code when the error originated from an HTTP API call.
        /// </summary>
        public int? HttpStatusCode { get; }

        /// <summary>
        ///     Creates a new SessionError event.
        /// </summary>
        public SessionError(
            string errorCode,
            string message,
            string sessionId,
            DateTime timestamp,
            bool isRecoverable = false,
            Exception exception = null,
            SessionErrorStage stage = SessionErrorStage.Unknown,
            int? httpStatusCode = null)
        {
            ErrorCode = errorCode;
            Message = message;
            SessionId = sessionId;
            Timestamp = timestamp;
            IsRecoverable = isRecoverable;
            Exception = exception;
            Stage = stage;
            HttpStatusCode = httpStatusCode;
        }

        /// <summary>
        ///     Creates a SessionError event with the current UTC timestamp.
        /// </summary>
        /// <param name="errorCode">Hierarchical error code</param>
        /// <param name="message">Human-readable error message</param>
        /// <param name="sessionId">The session identifier</param>
        /// <param name="isRecoverable">Whether the error can be recovered from</param>
        /// <param name="exception">Optional exception that caused the error</param>
        /// <returns>A new SessionError event</returns>
        public static SessionError Create(
            string errorCode,
            string message,
            string sessionId = null,
            bool isRecoverable = false,
            Exception exception = null,
            SessionErrorStage stage = SessionErrorStage.Unknown,
            int? httpStatusCode = null)
        {
            return new SessionError(
                errorCode,
                message,
                sessionId,
                DateTime.UtcNow,
                isRecoverable,
                exception,
                stage,
                httpStatusCode
            );
        }

        /// <summary>
        ///     Checks if this is a connection-related error.
        /// </summary>
        public bool IsConnectionError =>
            ErrorCode?.StartsWith("connection.") ?? false;

        /// <summary>
        ///     Checks if this is a session-related error.
        /// </summary>
        public bool IsSessionError =>
            ErrorCode?.StartsWith("session.") ?? false;

        /// <summary>
        ///     Checks if this is a transport-related error.
        /// </summary>
        public bool IsTransportError =>
            ErrorCode?.StartsWith("transport.") ?? false;

        /// <summary>
        ///     Checks if this is a protocol-related error.
        /// </summary>
        public bool IsProtocolError =>
            ErrorCode?.StartsWith("protocol.") ?? false;

        /// <summary>
        ///     Checks if this is a server/pipeline-related error.
        /// </summary>
        public bool IsServerError =>
            ErrorCode?.StartsWith("server.") ?? false;

        /// <summary>
        ///     Gets a short error category from the error code (e.g., "connection" from "connection.timeout").
        /// </summary>
        public string Category
        {
            get
            {
                if (string.IsNullOrEmpty(ErrorCode))
                    return "unknown";

                int dotIndex = ErrorCode.IndexOf('.');
                return dotIndex > 0 ? ErrorCode.Substring(0, dotIndex) : ErrorCode;
            }
        }
    }
}
