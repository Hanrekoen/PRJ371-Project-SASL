namespace Convai.Runtime.Core.Coordinators
{
    /// <summary>
    ///     Provides diagnostic information and metrics collection for room sessions.
    ///     Extracted from ConvaiRoomManager to enable focused testing and clear responsibility boundaries.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This interface provides observability into room connection health and session metrics.
    ///         It is primarily used for debugging, monitoring, and support diagnostics.
    ///     </para>
    ///     <para>
    ///         <b>Non-Blocking Design</b>: All methods are synchronous and return cached/computed data.
    ///         They should not perform blocking operations.
    ///     </para>
    /// </remarks>
    public interface IRoomDiagnostics
    {
        /// <summary>
        ///     Gets the current diagnostic snapshot for the room session.
        /// </summary>
        /// <returns>Immutable snapshot of current diagnostics.</returns>
        public RoomDiagnosticsSnapshot GetDiagnostics();

        /// <summary>
        ///     Records a connection attempt for metrics tracking.
        /// </summary>
        /// <param name="success">Whether the connection attempt succeeded.</param>
        public void RecordConnectionAttempt(bool success);

        /// <summary>
        ///     Records an error for diagnostics tracking.
        /// </summary>
        /// <param name="errorCode">Error code identifying the error type.</param>
        /// <param name="message">Human-readable error message.</param>
        public void RecordError(string errorCode, string message);

        /// <summary>
        ///     Records a warning (non-fatal issue) for diagnostics tracking.
        /// </summary>
        /// <param name="warningCode">Warning code identifying the issue type.</param>
        /// <param name="message">Human-readable warning message.</param>
        public void RecordWarning(string warningCode, string message);

        /// <summary>
        ///     Resets all accumulated metrics.
        ///     Typically called when starting a new session.
        /// </summary>
        public void Reset();
    }
}
