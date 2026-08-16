namespace Convai.Infrastructure.Protocol.Messages
{
    /// <summary>Outbound user text message.</summary>
    public class RTVIUserTextMessage : RTVISendMessageBase
    {
        internal string MessageId { get; }
        internal string Text { get; }

        /// <summary>
        ///     Initializes a new instance of the <see cref="RTVIUserTextMessage" /> class.
        /// </summary>
        /// <param name="text">User text to send.</param>
        public RTVIUserTextMessage(string text)
            : this(text, System.Guid.NewGuid().ToString("N"))
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="RTVIUserTextMessage" /> class with a stable message ID.
        /// </summary>
        /// <param name="text">User text to send.</param>
        /// <param name="messageId">Stable message identifier used for local echo reconciliation.</param>
        public RTVIUserTextMessage(string text, string messageId)
        {
            string stableMessageId = string.IsNullOrWhiteSpace(messageId)
                ? System.Guid.NewGuid().ToString("N")
                : messageId;

            Type = "user_text_message";
            Id = stableMessageId;
            MessageId = stableMessageId;
            Text = text ?? string.Empty;
            Data = new { text, message_id = stableMessageId };
        }
    }
}
