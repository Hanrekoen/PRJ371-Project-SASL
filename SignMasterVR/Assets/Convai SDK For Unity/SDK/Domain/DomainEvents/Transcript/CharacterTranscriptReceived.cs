using System;
using Convai.Domain.Models;

namespace Convai.Domain.DomainEvents.Transcript
{
    /// <summary>
    ///     Domain event raised when a character transcript is received.
    /// </summary>
    public readonly struct CharacterTranscriptReceived
    {
        /// <summary>
        ///     The transcript message from the character.
        /// </summary>
        public TranscriptMessage Message { get; }

        /// <summary>
        ///     The speaking turn/session identifier associated with this transcript update.
        /// </summary>
        public string TurnId { get; }

        /// <summary>
        ///     Unique identifier for the rendered transcript message.
        /// </summary>
        public string MessageId { get; }

        /// <summary>
        ///     Optional backend response identifier associated with this character text.
        /// </summary>
        public string ResponseId { get; }

        /// <summary>
        ///     Normalized source of the transcript event.
        /// </summary>
        public TranscriptSegmentSourceKind SourceKind { get; }

        /// <summary>
        ///     The lifecycle this event represents.
        /// </summary>
        public TranscriptLifecycle Lifecycle { get; }

        /// <summary>
        ///     Unique inbound update identifier used for idempotency.
        /// </summary>
        public string UpdateId { get; }

        /// <summary>
        ///     Whether backend output is intended to be spoken.
        /// </summary>
        public bool IsSpoken { get; }

        /// <summary>
        ///     Backend aggregation mode, when supplied.
        /// </summary>
        public string AggregatedBy { get; }

        /// <summary>
        ///     Creates a new CharacterTranscriptReceived event.
        /// </summary>
        public CharacterTranscriptReceived(
            TranscriptMessage message,
            string turnId = null,
            string messageId = null,
            string responseId = null,
            TranscriptSegmentSourceKind sourceKind = TranscriptSegmentSourceKind.BotOutput,
            TranscriptLifecycle? lifecycle = null,
            string updateId = null,
            bool isSpoken = true,
            string aggregatedBy = null)
        {
            Message = message;
            ResponseId = responseId ?? string.Empty;
            TurnId = string.IsNullOrWhiteSpace(turnId)
                ? ResolveFirstNonEmpty(messageId, ResponseId)
                : turnId;
            MessageId = string.IsNullOrWhiteSpace(messageId)
                ? ResolveFirstNonEmpty(TurnId, ResponseId)
                : messageId;
            SourceKind = sourceKind;
            Lifecycle = lifecycle ?? (message.IsFinal ? TranscriptLifecycle.Stable : TranscriptLifecycle.Streaming);
            UpdateId = updateId ?? string.Empty;
            IsSpoken = isSpoken;
            AggregatedBy = aggregatedBy ?? string.Empty;
        }

        /// <summary>
        ///     Creates a CharacterTranscriptReceived event from individual parameters.
        /// </summary>
        /// <param name="characterId">The character's unique identifier</param>
        /// <param name="displayName">The character's display name</param>
        /// <param name="text">The transcript text</param>
        /// <param name="isFinal">Whether this is a final transcript</param>
        /// <param name="confidence">Optional confidence score</param>
        /// <returns>A new CharacterTranscriptReceived event</returns>
        public static CharacterTranscriptReceived Create(
            string characterId,
            string displayName,
            string text,
            bool isFinal,
            float? confidence = null,
            string turnId = null,
            string messageId = null,
            string responseId = null)
        {
            var message = TranscriptMessage.Create(
                characterId,
                displayName,
                text,
                isFinal,
                confidence,
                speakerType: SpeakerType.Character
            );

            return new CharacterTranscriptReceived(
                message,
                turnId,
                messageId,
                responseId,
                lifecycle: isFinal ? TranscriptLifecycle.Stable : TranscriptLifecycle.Streaming);
        }

        /// <summary>
        ///     Gets the character ID from the message.
        /// </summary>
        public string CharacterId => Message.PlayerOrCharacterId;

        /// <summary>
        ///     Gets the character's display name from the message.
        /// </summary>
        public string CharacterName => Message.DisplayName;

        /// <summary>
        ///     Gets the transcript text from the message.
        /// </summary>
        public string Text => Message.Text;

        /// <summary>
        ///     Checks if this is a final transcript.
        /// </summary>
        public bool IsFinal => Lifecycle != TranscriptLifecycle.Streaming;

        /// <summary>
        ///     Checks if this is an interim transcript.
        /// </summary>
        public bool IsInterim => Lifecycle == TranscriptLifecycle.Streaming;

        /// <summary>
        ///     Gets the timestamp when the transcript was received.
        /// </summary>
        public DateTime Timestamp => Message.Timestamp;

        private static string ResolveFirstNonEmpty(params string[] values)
        {
            if (values == null) return string.Empty;

            foreach (string value in values)
                if (!string.IsNullOrWhiteSpace(value))
                    return value;

            return string.Empty;
        }
    }
}
