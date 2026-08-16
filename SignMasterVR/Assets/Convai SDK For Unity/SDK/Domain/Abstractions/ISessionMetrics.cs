using System;

namespace Convai.Domain.Abstractions
{
    /// <summary>
    ///     Interface for tracking session-level metrics and telemetry.
    ///     Provides insights into session health, reconnection patterns, and overall reliability.
    /// </summary>
    /// <remarks>
    ///     All properties and methods are thread-safe.
    ///     Metrics are in-memory only and reset on application restart.
    ///     Key metrics tracked:
    ///     - Session duration (time spent in Connected state)
    ///     - Reconnection counts and success rates
    ///     - Session lifecycle events
    ///     Usage:
    ///     <code>
    /// 
    /// private readonly ISessionMetrics _sessionMetrics;
    /// 
    /// 
    /// TimeSpan duration = _sessionMetrics.CurrentSessionDuration;
    /// float successRate = _sessionMetrics.ReconnectionSuccessRate;
    /// 
    /// 
    /// _sessionMetrics.Reset();
    /// </code>
    /// </remarks>
    public interface ISessionMetrics
    {
        #region Snapshot

        /// <summary>
        ///     Gets a snapshot of the current metrics.
        ///     Useful for logging or reporting.
        /// </summary>
        /// <returns>A snapshot containing all current metric values.</returns>
        public SessionMetricsSnapshot GetSnapshot();

        #endregion

        #region Control

        /// <summary>
        ///     Resets all metrics to their initial values.
        ///     Call this when starting a fresh session or for testing purposes.
        /// </summary>
        public void Reset();

        #endregion

        #region Session Duration

        /// <summary>
        ///     Total duration of the current session (time in Connected state).
        ///     Pauses during Reconnecting and resumes upon successful reconnection.
        ///     Returns TimeSpan.Zero if no session is active.
        /// </summary>
        public TimeSpan CurrentSessionDuration { get; }

        /// <summary>
        ///     Total accumulated connected time across all sessions since last reset.
        /// </summary>
        public TimeSpan TotalConnectedTime { get; }

        /// <summary>
        ///     Timestamp when the current session started, or null if not connected.
        /// </summary>
        public DateTime? SessionStartTime { get; }

        /// <summary>
        ///     Timestamp when the session ended (transitioned to Disconnected), or null if still active.
        /// </summary>
        public DateTime? SessionEndTime { get; }

        #endregion

        #region Reconnection Metrics

        /// <summary>
        ///     Total number of reconnection attempts during the current session.
        /// </summary>
        public int ReconnectionAttempts { get; }

        /// <summary>
        ///     Number of successful reconnections during the current session.
        /// </summary>
        public int SuccessfulReconnections { get; }

        /// <summary>
        ///     Number of failed reconnections (transitions from Reconnecting to Error).
        /// </summary>
        public int FailedReconnections { get; }

        /// <summary>
        ///     Reconnection success rate as a value between 0.0 and 1.0.
        ///     Returns 1.0 if no reconnection attempts have been made.
        /// </summary>
        public float ReconnectionSuccessRate { get; }

        #endregion

        #region Session Lifecycle

        /// <summary>
        ///     Total number of sessions started since last reset.
        /// </summary>
        public int TotalSessionsStarted { get; }

        /// <summary>
        ///     Number of sessions that ended cleanly (via Disconnecting -> Disconnected).
        /// </summary>
        public int CleanDisconnections { get; }

        /// <summary>
        ///     Number of sessions that ended in error state.
        /// </summary>
        public int ErrorDisconnections { get; }

        /// <summary>
        ///     The last error code encountered, or null if no error.
        /// </summary>
        public string LastErrorCode { get; }

        /// <summary>
        ///     Timestamp of the last error, or null if no error occurred.
        /// </summary>
        public DateTime? LastErrorTime { get; }

        #endregion
    }

    /// <summary>
    ///     Immutable snapshot of session metrics for logging and reporting.
    /// </summary>
    public readonly struct SessionMetricsSnapshot
    {
        /// <summary>
        ///     Total duration spent in Connected state.
        /// </summary>
        public TimeSpan TotalConnectedTime { get; }

        /// <summary>
        ///     Number of reconnection attempts.
        /// </summary>
        public int ReconnectionAttempts { get; }

        /// <summary>
        ///     Number of successful reconnections.
        /// </summary>
        public int SuccessfulReconnections { get; }

        /// <summary>
        ///     Number of failed reconnections.
        /// </summary>
        public int FailedReconnections { get; }

        /// <summary>
        ///     Reconnection success rate (0.0 to 1.0).
        /// </summary>
        public float ReconnectionSuccessRate { get; }

        /// <summary>
        ///     Total number of sessions started.
        /// </summary>
        public int TotalSessionsStarted { get; }

        /// <summary>
        ///     Number of clean disconnections.
        /// </summary>
        public int CleanDisconnections { get; }

        /// <summary>
        ///     Number of error disconnections.
        /// </summary>
        public int ErrorDisconnections { get; }

        /// <summary>
        ///     Last error code, or null.
        /// </summary>
        public string LastErrorCode { get; }

        /// <summary>
        ///     Timestamp when this snapshot was taken.
        /// </summary>
        public DateTime SnapshotTime { get; }

        /// <summary>
        ///     Creates a new metrics snapshot.
        /// </summary>
        public SessionMetricsSnapshot(
            TimeSpan totalConnectedTime,
            int reconnectionAttempts,
            int successfulReconnections,
            int failedReconnections,
            float reconnectionSuccessRate,
            int totalSessionsStarted,
            int cleanDisconnections,
            int errorDisconnections,
            string lastErrorCode)
        {
            TotalConnectedTime = totalConnectedTime;
            ReconnectionAttempts = reconnectionAttempts;
            SuccessfulReconnections = successfulReconnections;
            FailedReconnections = failedReconnections;
            ReconnectionSuccessRate = reconnectionSuccessRate;
            TotalSessionsStarted = totalSessionsStarted;
            CleanDisconnections = cleanDisconnections;
            ErrorDisconnections = errorDisconnections;
            LastErrorCode = lastErrorCode;
            SnapshotTime = DateTime.UtcNow;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"SessionMetrics[Connected={TotalConnectedTime:hh\\:mm\\:ss}, " +
                   $"Reconnects={SuccessfulReconnections}/{ReconnectionAttempts} ({ReconnectionSuccessRate:P0}), " +
                   $"Sessions={TotalSessionsStarted}, " +
                   $"Clean={CleanDisconnections}, Errors={ErrorDisconnections}]";
        }
    }
}
