using System;

namespace Convai.Domain.Models
{
    /// <summary>
    ///     Canonical transcript update payload shared by single-player and multiplayer flows.
    /// </summary>
    public readonly struct TranscriptUpdate
    {
        public TranscriptUpdate(
            string messageId,
            string turnId,
            string responseId,
            SpeakerType speakerType,
            string playerOrCharacterId,
            string displayName,
            string participantId,
            string text,
            TranscriptLifecycle lifecycle,
            TranscriptSegmentSourceKind sourceKind,
            DateTime timestamp)
        {
            MessageId = messageId ?? string.Empty;
            TurnId = turnId ?? string.Empty;
            ResponseId = responseId ?? string.Empty;
            SpeakerType = speakerType;
            PlayerOrCharacterId = playerOrCharacterId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            ParticipantId = participantId ?? string.Empty;
            Text = text ?? string.Empty;
            Lifecycle = lifecycle;
            SourceKind = sourceKind;
            Timestamp = timestamp == default ? DateTime.UtcNow : timestamp;
        }

        public string MessageId { get; }

        public string TurnId { get; }

        public string ResponseId { get; }

        public SpeakerType SpeakerType { get; }

        public string PlayerOrCharacterId { get; }

        public string DisplayName { get; }

        public string ParticipantId { get; }

        public string Text { get; }

        public TranscriptLifecycle Lifecycle { get; }

        public bool IsFinal => Lifecycle != TranscriptLifecycle.Streaming;

        public TranscriptSegmentSourceKind SourceKind { get; }

        public DateTime Timestamp { get; }

        public bool HasSpeakerInfo => !string.IsNullOrWhiteSpace(ParticipantId);

        public bool IsEmpty => string.IsNullOrWhiteSpace(Text);

        public bool IsValid => !string.IsNullOrWhiteSpace(MessageId) ||
                               !string.IsNullOrWhiteSpace(TurnId) ||
                               !string.IsNullOrWhiteSpace(Text);

        public TranscriptMessage ToMessage()
        {
            return new TranscriptMessage(
                PlayerOrCharacterId,
                DisplayName,
                Text,
                IsFinal,
                Timestamp,
                participantId: ParticipantId,
                speakerType: SpeakerType);
        }

        public static TranscriptUpdate FromTurn(TranscriptTurnSnapshot turn)
        {
            if (turn == null) return default;

            SpeakerType speakerType = turn.Participant.Kind == TranscriptParticipantKind.Player
                ? SpeakerType.Player
                : SpeakerType.Character;

            TranscriptSegmentSourceKind sourceKind = TranscriptSegmentSourceKind.Unknown;
            if (turn.Segments != null && turn.Segments.Count > 0)
                sourceKind = turn.Segments[turn.Segments.Count - 1].SourceKind;

            return new TranscriptUpdate(
                turn.MessageId,
                turn.TurnId,
                turn.ResponseId,
                speakerType,
                turn.Participant.PlayerOrCharacterId,
                turn.Participant.DisplayName,
                turn.Participant.ParticipantId,
                turn.DisplayText,
                turn.Lifecycle,
                sourceKind,
                turn.LastUpdatedAtUtc);
        }
    }
}
