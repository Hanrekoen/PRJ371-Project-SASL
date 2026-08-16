using System;

namespace Convai.Domain.DomainEvents.Runtime
{
    /// <summary>
    ///     Domain event raised when the backend returns a content moderation result.
    ///     When <see cref="WasFlagged" /> is <c>true</c>, the user's input was flagged
    ///     and the character response may be replaced with a refusal.
    /// </summary>
    /// <remarks>
    ///     Subscribe via EventHub or <c>ConvaiManager.Events.OnModerationResponseReceived</c>.
    ///     <code>
    /// _eventHub.Subscribe&lt;ModerationResponseReceived&gt;(this, e =&gt;
    /// {
    ///     if (e.WasFlagged)
    ///     {
    ///         Debug.LogWarning($"Input flagged: {e.Reason}");
    ///         ShowModerationWarning(e.Reason);
    ///     }
    /// });
    /// </code>
    /// </remarks>
    public readonly struct ModerationResponseReceived
    {
        /// <summary>Whether the user input was flagged by moderation.</summary>
        public bool WasFlagged { get; }

        /// <summary>The user input that was evaluated.</summary>
        public string UserInput { get; }

        /// <summary>Optional reason for the moderation decision.</summary>
        public string Reason { get; }

        /// <summary>When the event occurred (UTC).</summary>
        public DateTime Timestamp { get; }

        /// <summary>Creates a new ModerationResponseReceived event.</summary>
        public ModerationResponseReceived(bool wasFlagged, string userInput, string reason, DateTime timestamp)
        {
            WasFlagged = wasFlagged;
            UserInput = userInput ?? string.Empty;
            Reason = reason ?? string.Empty;
            Timestamp = timestamp;
        }

        /// <summary>Creates a ModerationResponseReceived event with the current UTC timestamp.</summary>
        public static ModerationResponseReceived Create(bool wasFlagged, string userInput, string reason) =>
            new(wasFlagged, userInput, reason, DateTime.UtcNow);
    }
}
