using System;

namespace Convai.Domain.DomainEvents.Runtime
{
    /// <summary>
    ///     Domain event raised when an outbound RTVI message is serialized and queued for transport.
    /// </summary>
    public readonly struct OutboundRtviMessageSent
    {
        /// <summary>The RTVI message type discriminator.</summary>
        public string MessageType { get; }

        /// <summary>The RTVI message identifier.</summary>
        public string MessageId { get; }

        /// <summary>When the outbound message was queued (UTC).</summary>
        public DateTime Timestamp { get; }

        /// <summary>Creates a new <see cref="OutboundRtviMessageSent" /> event.</summary>
        public OutboundRtviMessageSent(string messageType, string messageId, DateTime timestamp)
        {
            MessageType = messageType ?? string.Empty;
            MessageId = messageId ?? string.Empty;
            Timestamp = timestamp;
        }

        /// <summary>Creates a new event with the current UTC timestamp.</summary>
        public static OutboundRtviMessageSent Create(string messageType, string messageId) =>
            new(messageType, messageId, DateTime.UtcNow);
    }
}
