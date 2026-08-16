using System;

namespace Convai.Domain.DomainEvents.Session
{
    /// <summary>
    ///     Domain event raised when the backend reports a usage quota has been exhausted.
    ///     The pipeline is terminated immediately after this message.
    /// </summary>
    /// <remarks>
    ///     Subscribe via EventHub or <c>ConvaiManager.Events.OnUsageLimitReached</c>.
    ///     <code>
    /// _eventHub.Subscribe&lt;UsageLimitReached&gt;(this, e =&gt;
    /// {
    ///     Debug.LogWarning($"Quota exceeded ({e.QuotaType}): {e.Message}");
    ///     ShowUpgradePrompt();
    /// });
    /// </code>
    /// </remarks>
    public readonly struct UsageLimitReached
    {
        /// <summary>The type of quota that was exceeded (e.g., "daily", "monthly", "additional").</summary>
        public string QuotaType { get; }

        /// <summary>Human-readable description of the limit breach.</summary>
        public string Message { get; }

        /// <summary>When the event occurred (UTC).</summary>
        public DateTime Timestamp { get; }

        /// <summary>Creates a new UsageLimitReached event.</summary>
        public UsageLimitReached(string quotaType, string message, DateTime timestamp)
        {
            QuotaType = quotaType ?? string.Empty;
            Message = message ?? string.Empty;
            Timestamp = timestamp;
        }

        /// <summary>Creates a UsageLimitReached event with the current UTC timestamp.</summary>
        public static UsageLimitReached Create(string quotaType, string message) =>
            new(quotaType, message, DateTime.UtcNow);
    }
}
