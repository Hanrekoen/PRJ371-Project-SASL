using System;
using Convai.Domain.Models;

namespace Convai.Domain.DomainEvents.Transcript
{
    /// <summary>
    ///     Domain event raised when a player transcript is received.
    /// </summary>
    public readonly struct PlayerTranscriptReceived
    {
        /// <summary>
        ///     The transcript message from the player.
        /// </summary>
        public TranscriptMessage Message { get; }

        /// <summary>
        ///     The transcription phase this event represents.
        /// </summary>
        public TranscriptionPhase Phase { get; }

        /// <summary>
        ///     Speaker attribution data for multi-user scenarios.
        /// </summary>
        public SpeakerInfo SpeakerInfo { get; }

        /// <summary>
        ///     The speaking turn/session identifier associated with this transcript update.
        /// </summary>
        public string TurnId { get; }

        /// <summary>
        ///     Unique identifier for the rendered transcript message.
        /// </summary>
        public string MessageId { get; }

        /// <summary>
        ///     Normalized source of the transcript event.
        /// </summary>
        public TranscriptSegmentSourceKind SourceKind { get; }

        /// <summary>
        ///     Creates a new PlayerTranscriptReceived event.
        /// </summary>
        public PlayerTranscriptReceived(
            TranscriptMessage message,
            TranscriptionPhase phase = TranscriptionPhase.Interim,
            SpeakerInfo speakerInfo = default,
            string turnId = null,
            string messageId = null,
            TranscriptSegmentSourceKind sourceKind = TranscriptSegmentSourceKind.Unknown)
        {
            Message = message;
            Phase = phase;
            SpeakerInfo = speakerInfo.IsValid ? speakerInfo : SpeakerInfo.Empty;
            TurnId = string.IsNullOrWhiteSpace(turnId) ? message.PlayerOrCharacterId : turnId;
            MessageId = string.IsNullOrWhiteSpace(messageId) ? TurnId : messageId;
            SourceKind = sourceKind == TranscriptSegmentSourceKind.Unknown
                ? GetDefaultSourceKind(phase)
                : sourceKind;
        }

        /// <summary>
        ///     Creates a PlayerTranscriptReceived event from individual parameters.
        /// </summary>
        /// <param name="playerId">The player's unique identifier</param>
        /// <param name="displayName">The player's display name</param>
        /// <param name="text">The transcript text</param>
        /// <param name="isFinal">Whether this is a final transcript</param>
        /// <param name="phase">The transcription phase</param>
        /// <param name="confidence">Optional confidence score from speech recognition</param>
        /// <param name="speakerInfo">Optional speaker attribution data</param>
        /// <returns>A new PlayerTranscriptReceived event</returns>
        public static PlayerTranscriptReceived Create(
            string playerId,
            string displayName,
            string text,
            bool isFinal,
            TranscriptionPhase phase = TranscriptionPhase.Interim,
            float? confidence = null,
            SpeakerInfo speakerInfo = default,
            string turnId = null,
            string messageId = null,
            TranscriptSegmentSourceKind sourceKind = TranscriptSegmentSourceKind.Unknown)
        {
            var message = TranscriptMessage.Create(
                playerId,
                displayName,
                text,
                isFinal,
                confidence,
                speakerInfo.ParticipantId,
                SpeakerType.Player
            );

            return new PlayerTranscriptReceived(message, phase, speakerInfo, turnId, messageId, sourceKind);
        }

        /// <summary>
        ///     Creates a PlayerTranscriptReceived event with speaker info.
        /// </summary>
        public static PlayerTranscriptReceived CreateWithSpeaker(
            string text,
            TranscriptionPhase phase,
            SpeakerInfo speakerInfo,
            string turnId = null,
            string messageId = null,
            TranscriptSegmentSourceKind sourceKind = TranscriptSegmentSourceKind.Unknown)
        {
            TranscriptMessage message = TranscriptMessage.ForPlayer(
                text,
                phase == TranscriptionPhase.ProcessedFinal || phase == TranscriptionPhase.Completed,
                speakerInfo.SpeakerId,
                speakerInfo.SpeakerName,
                speakerInfo.ParticipantId
            );

            return new PlayerTranscriptReceived(message, phase, speakerInfo, turnId, messageId, sourceKind);
        }

        /// <summary>
        ///     Creates a typed-text player transcript event keyed by a stable message ID.
        /// </summary>
        public static PlayerTranscriptReceived CreateTypedText(
            string playerId,
            string displayName,
            string text,
            string messageId,
            SpeakerInfo speakerInfo = default)
        {
            return Create(
                playerId,
                displayName,
                text,
                true,
                TranscriptionPhase.Completed,
                speakerInfo: speakerInfo,
                turnId: messageId,
                messageId: messageId,
                sourceKind: TranscriptSegmentSourceKind.PlayerTypedText);
        }

        /// <summary>
        ///     Gets the player ID from the message.
        /// </summary>
        public string PlayerId => Message.PlayerOrCharacterId;

        /// <summary>
        ///     Gets the player's display name from the message.
        /// </summary>
        public string PlayerName => Message.DisplayName;

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
        ///     Gets the lifecycle of this player transcript update.
        /// </summary>
        public TranscriptLifecycle Lifecycle => Phase switch
        {
            TranscriptionPhase.AsrFinal => TranscriptLifecycle.Stable,
            TranscriptionPhase.ProcessedFinal => TranscriptLifecycle.Stable,
            TranscriptionPhase.Completed => TranscriptLifecycle.Completed,
            _ => TranscriptLifecycle.Streaming
        };

        /// <summary>
        ///     Gets the timestamp when the transcript was received.
        /// </summary>
        public DateTime Timestamp => Message.Timestamp;

        /// <summary>
        ///     Gets the confidence score from speech recognition, if available.
        /// </summary>
        public float? Confidence => Message.Confidence;

        /// <summary>
        ///     Gets whether multi-user speaker attribution is available.
        /// </summary>
        public bool HasSpeakerInfo => SpeakerInfo.IsValid;

        /// <summary>
        ///     Gets the LiveKit participant ID, if available.
        /// </summary>
        public string ParticipantId => Message.ParticipantId;

        private static TranscriptSegmentSourceKind GetDefaultSourceKind(TranscriptionPhase phase) => phase switch
        {
            TranscriptionPhase.ProcessedFinal => TranscriptSegmentSourceKind.PlayerProcessedFinal,
            _ => TranscriptSegmentSourceKind.PlayerAsr
        };
    }
}
