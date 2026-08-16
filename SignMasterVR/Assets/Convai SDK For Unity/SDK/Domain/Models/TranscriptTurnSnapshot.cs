using System;
using System.Collections.Generic;

namespace Convai.Domain.Models
{
    /// <summary>
    ///     Immutable room-scoped transcript turn snapshot.
    /// </summary>
    public sealed class TranscriptTurnSnapshot
    {
        public TranscriptTurnSnapshot(
            string turnId,
            long roomSequence,
            TranscriptParticipantRef participant,
            DateTime startedAtUtc,
            DateTime lastUpdatedAtUtc,
            DateTime? completedAtUtc,
            TranscriptLifecycle lifecycle,
            string committedText,
            string interimText,
            bool wasInterrupted,
            IReadOnlyList<TranscriptSegmentSnapshot> segments,
            string responseId = null,
            string conversationTargetCharacterId = null,
            TranscriptTurnState? state = null,
            TranscriptTextSource primaryTextSource = TranscriptTextSource.Unknown,
            int revision = 0)
        {
            TurnId = turnId ?? string.Empty;
            RoomSequence = roomSequence;
            Participant = participant;
            StartedAtUtc = startedAtUtc;
            LastUpdatedAtUtc = lastUpdatedAtUtc;
            CompletedAtUtc = completedAtUtc;
            Lifecycle = lifecycle;
            CommittedText = committedText ?? string.Empty;
            InterimText = interimText ?? string.Empty;
            WasInterrupted = wasInterrupted;
            Segments = segments ?? Array.Empty<TranscriptSegmentSnapshot>();
            ResponseId = responseId ?? string.Empty;
            ConversationTargetCharacterId = conversationTargetCharacterId ?? string.Empty;
            State = state ?? TranscriptModelMapper.MapState(lifecycle, wasInterrupted);
            PrimaryTextSource = primaryTextSource;
            Revision = revision;
        }

        public string TurnId { get; }

        public string MessageId => TurnId;

        public string ResponseId { get; }

        public long RoomSequence { get; }

        public TranscriptParticipantRef Participant { get; }

        public DateTime StartedAtUtc { get; }

        public DateTime LastUpdatedAtUtc { get; }

        public DateTime? CompletedAtUtc { get; }

        public TranscriptLifecycle Lifecycle { get; }

        public TranscriptTurnState State { get; }

        public TranscriptTextSource PrimaryTextSource { get; }

        public int Revision { get; }

        public string CommittedText { get; }

        public string InterimText { get; }

        public string DisplayText => TranscriptTextMerge.Append(CommittedText, InterimText);

        public bool WasInterrupted { get; }

        public IReadOnlyList<TranscriptSegmentSnapshot> Segments { get; }

        public string ConversationTargetCharacterId { get; }

        public bool HasText => !string.IsNullOrWhiteSpace(DisplayText);

    }
}
