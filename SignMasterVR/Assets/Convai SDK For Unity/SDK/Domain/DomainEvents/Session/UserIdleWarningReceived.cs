using System;

namespace Convai.Domain.DomainEvents.Session
{
    /// <summary>
    ///     Domain event raised when backend sends a user idle warning before inactivity disconnect.
    /// </summary>
    public readonly struct UserIdleWarningReceived
    {
        /// <summary>Seconds remaining before idle disconnection.</summary>
        public int RemainingSeconds { get; }

        /// <summary>Optional user-facing warning message.</summary>
        public string Message { get; }

        /// <summary>When the warning was received (UTC).</summary>
        public DateTime Timestamp { get; }

        /// <summary>Creates a new UserIdleWarningReceived event.</summary>
        public UserIdleWarningReceived(int remainingSeconds, string message, DateTime timestamp)
        {
            RemainingSeconds = Math.Max(0, remainingSeconds);
            Message = message ?? string.Empty;
            Timestamp = timestamp;
        }

        /// <summary>Creates a UserIdleWarningReceived event with the current UTC timestamp.</summary>
        public static UserIdleWarningReceived Create(int remainingSeconds, string message) =>
            new(remainingSeconds, message, DateTime.UtcNow);
    }

    /// <summary>
    ///     Domain event raised when the client-side deadline from the latest
    ///     <see cref="UserIdleWarningReceived" /> countdown elapses without observed user activity.
    /// </summary>
    /// <remarks>
    ///     This is a local deadline signal for UI and recovery workflows. The backend currently does not send a
    ///     separate timeout packet, so this event does not claim that transport disconnection has completed.
    /// </remarks>
    public readonly struct UserIdleTimeoutElapsed
    {
        public UserIdleTimeoutElapsed(DateTime warningReceivedAt, DateTime deadlineUtc, DateTime timestamp)
        {
            WarningReceivedAt = warningReceivedAt;
            DeadlineUtc = deadlineUtc;
            Timestamp = timestamp;
        }

        /// <summary>When the warning that established this deadline was received (UTC).</summary>
        public DateTime WarningReceivedAt { get; }

        /// <summary>The deadline calculated from the server-provided remaining seconds.</summary>
        public DateTime DeadlineUtc { get; }

        /// <summary>When the client observed that the deadline elapsed (UTC).</summary>
        public DateTime Timestamp { get; }

        public static UserIdleTimeoutElapsed Create(DateTime warningReceivedAt, DateTime deadlineUtc) =>
            new(warningReceivedAt, deadlineUtc, DateTime.UtcNow);
    }
}
