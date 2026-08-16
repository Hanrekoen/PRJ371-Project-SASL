using System;
using System.Collections.Generic;
using System.Linq;

namespace Convai.Domain.Models
{
    public enum TranscriptTurnState
    {
        Listening = 0,
        Streaming = 1,
        Stable = 2,
        Committed = 4,
        Interrupted = 5,
        Discarded = 6
    }

    public enum TranscriptTextSource
    {
        Unknown = 0,
        InterimAsr = 1,
        AsrFinal = 2,
        ProcessedFinal = 3,
        TypedText = 4,
        BotOutput = 5,
        BotPreview = 6,
        LegacyBotTranscript = 7
    }

    public enum TranscriptSpeakerType
    {
        Player = 0,
        Character = 1,
        System = 2
    }

    public enum TranscriptChangeKind
    {
        Added = 0,
        Updated = 1,
        Committed = 2,
        Interrupted = 3,
        Corrected = 4,
        Removed = 5
    }

    public enum TranscriptCaptionState
    {
        Streaming = 0,
        Stable = 1,
        Completed = 2,
        Interrupted = 3
    }

    public enum TranscriptExportFormat
    {
        PlainText = 0,
        Markdown = 1,
        Json = 2
    }

    public sealed class TranscriptSpeaker
    {
        public TranscriptSpeaker(
            TranscriptSpeakerType type,
            string id,
            string displayName,
            string participantId = null)
        {
            Type = type;
            Id = id ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            ParticipantId = participantId ?? string.Empty;
        }

        public TranscriptSpeakerType Type { get; }
        public string Id { get; }
        public string DisplayName { get; }
        public string ParticipantId { get; }

        public static TranscriptSpeaker FromParticipant(TranscriptParticipantRef participant)
        {
            TranscriptSpeakerType type = participant.Kind == TranscriptParticipantKind.Player
                ? TranscriptSpeakerType.Player
                : TranscriptSpeakerType.Character;

            return new TranscriptSpeaker(
                type,
                participant.PlayerOrCharacterId,
                participant.DisplayName,
                participant.ParticipantId);
        }
    }

    public sealed class TranscriptSegment
    {
        public TranscriptSegment(
            string id,
            string turnId,
            TranscriptSpeaker speaker,
            string stableText,
            string interimText,
            string displayText,
            TranscriptTurnState state,
            TranscriptTextSource source,
            DateTime startedAtUtc,
            DateTime updatedAtUtc,
            DateTime? stoppedAtUtc)
        {
            Id = id ?? string.Empty;
            TurnId = turnId ?? string.Empty;
            Speaker = speaker;
            StableText = stableText ?? string.Empty;
            InterimText = interimText ?? string.Empty;
            DisplayText = displayText ?? string.Empty;
            State = state;
            Source = source;
            StartedAtUtc = startedAtUtc;
            UpdatedAtUtc = updatedAtUtc;
            StoppedAtUtc = stoppedAtUtc;
        }

        public string Id { get; }
        public string TurnId { get; }
        public TranscriptSpeaker Speaker { get; }
        public string StableText { get; }
        public string InterimText { get; }
        public string DisplayText { get; }
        public TranscriptTurnState State { get; }
        public TranscriptTextSource Source { get; }
        public DateTime StartedAtUtc { get; }
        public DateTime UpdatedAtUtc { get; }
        public DateTime? StoppedAtUtc { get; }
    }

    public sealed class TranscriptTurn
    {
        public TranscriptTurn(
            string id,
            string messageId,
            string responseId,
            long roomSequence,
            int revision,
            TranscriptSpeaker speaker,
            TranscriptTurnState state,
            TranscriptTextSource primaryTextSource,
            string stableText,
            string interimText,
            string displayText,
            DateTime startedAtUtc,
            DateTime lastUpdatedAtUtc,
            DateTime? committedAtUtc,
            bool wasInterrupted,
            IReadOnlyList<TranscriptSegment> segments)
        {
            Id = id ?? string.Empty;
            MessageId = messageId ?? string.Empty;
            ResponseId = responseId ?? string.Empty;
            RoomSequence = roomSequence;
            Revision = revision;
            Speaker = speaker;
            State = state;
            PrimaryTextSource = primaryTextSource;
            StableText = stableText ?? string.Empty;
            InterimText = interimText ?? string.Empty;
            DisplayText = displayText ?? string.Empty;
            StartedAtUtc = startedAtUtc;
            LastUpdatedAtUtc = lastUpdatedAtUtc;
            CommittedAtUtc = committedAtUtc;
            WasInterrupted = wasInterrupted;
            Segments = segments ?? Array.Empty<TranscriptSegment>();
        }

        public string Id { get; }
        public string MessageId { get; }
        public string ResponseId { get; }
        public long RoomSequence { get; }
        public int Revision { get; }
        public TranscriptSpeaker Speaker { get; }
        public TranscriptTurnState State { get; }
        public TranscriptTextSource PrimaryTextSource { get; }
        public string StableText { get; }
        public string InterimText { get; }
        public string DisplayText { get; }
        public DateTime StartedAtUtc { get; }
        public DateTime LastUpdatedAtUtc { get; }
        public DateTime? CommittedAtUtc { get; }
        public bool WasInterrupted { get; }
        public IReadOnlyList<TranscriptSegment> Segments { get; }
        public bool HasText => !string.IsNullOrWhiteSpace(DisplayText);
        public bool IsCommitted => State == TranscriptTurnState.Committed || State == TranscriptTurnState.Interrupted;
    }

    public sealed class TranscriptTimeline
    {
        private TranscriptTurn[] _orderedTurns;

        public TranscriptTimeline(
            long cursor,
            IReadOnlyList<TranscriptTurn> activeTurns,
            IReadOnlyList<TranscriptTurn> committedTurns,
            IReadOnlyDictionary<string, TranscriptTurn> turnsById)
        {
            Cursor = cursor;
            ActiveTurns = activeTurns ?? Array.Empty<TranscriptTurn>();
            CommittedTurns = committedTurns ?? Array.Empty<TranscriptTurn>();
            TurnsById = turnsById ?? new Dictionary<string, TranscriptTurn>();
        }

        public static TranscriptTimeline Empty { get; } = new(
            0,
            Array.Empty<TranscriptTurn>(),
            Array.Empty<TranscriptTurn>(),
            new Dictionary<string, TranscriptTurn>());

        public long Cursor { get; }
        public IReadOnlyList<TranscriptTurn> ActiveTurns { get; }
        public IReadOnlyList<TranscriptTurn> CommittedTurns { get; }
        public IReadOnlyDictionary<string, TranscriptTurn> TurnsById { get; }
        public IReadOnlyList<TranscriptTurn> Turns => _orderedTurns ??= ActiveTurns.Concat(CommittedTurns)
            .OrderBy(turn => turn.RoomSequence)
            .ToArray();

        public static TranscriptTimeline FromSnapshot(TranscriptTimelineSnapshot snapshot)
        {
            if (snapshot == null) return Empty;

            TranscriptTurn[] activeTurns = snapshot.ActiveTurns
                .Select(TranscriptModelMapper.FromSnapshot)
                .ToArray();
            TranscriptTurn[] committedTurns = snapshot.CommittedTurns
                .Select(TranscriptModelMapper.FromSnapshot)
                .ToArray();
            Dictionary<string, TranscriptTurn> byId = activeTurns.Concat(committedTurns)
                .GroupBy(turn => turn.Id)
                .ToDictionary(group => group.Key, group => group.First());

            return new TranscriptTimeline(snapshot.Cursor, activeTurns, committedTurns, byId);
        }
    }

    /// <summary>
    ///     Live, speech-aligned text. Captions intentionally remain separate from durable chat history.
    /// </summary>
    public sealed class TranscriptCaption
    {
        public TranscriptCaption(
            string turnId,
            TranscriptSpeaker speaker,
            string text,
            TranscriptCaptionState state,
            DateTime updatedAtUtc)
        {
            TurnId = turnId ?? string.Empty;
            Speaker = speaker;
            Text = text ?? string.Empty;
            State = state;
            UpdatedAtUtc = updatedAtUtc;
        }

        public string TurnId { get; }
        public TranscriptSpeaker Speaker { get; }
        public string Text { get; }
        public TranscriptCaptionState State { get; }
        public DateTime UpdatedAtUtc { get; }
        public bool HasText => !string.IsNullOrWhiteSpace(Text);
        public bool IsFinal => State == TranscriptCaptionState.Completed || State == TranscriptCaptionState.Interrupted;
        public bool WasInterrupted => State == TranscriptCaptionState.Interrupted;
    }

    public sealed class TranscriptCaptionSnapshot
    {
        public TranscriptCaptionSnapshot(long cursor, IReadOnlyList<TranscriptCaption> captions)
        {
            Cursor = cursor;
            Captions = captions ?? Array.Empty<TranscriptCaption>();
        }

        public static TranscriptCaptionSnapshot Empty { get; } =
            new(0, Array.Empty<TranscriptCaption>());

        public long Cursor { get; }
        public IReadOnlyList<TranscriptCaption> Captions { get; }
    }

    public sealed class TranscriptChange
    {
        public TranscriptChange(TranscriptChangeKind kind, TranscriptTurn turn, string turnId = null)
        {
            Kind = kind;
            Turn = turn;
            TurnId = turnId ?? turn?.Id ?? string.Empty;
        }

        public TranscriptChangeKind Kind { get; }
        public TranscriptTurn Turn { get; }
        public string TurnId { get; }
    }

    public sealed class TranscriptChangeBatch
    {
        public TranscriptChangeBatch(TranscriptTimeline timeline, IReadOnlyList<TranscriptChange> changes)
        {
            Timeline = timeline ?? TranscriptTimeline.Empty;
            Changes = changes ?? Array.Empty<TranscriptChange>();
        }

        public TranscriptTimeline Timeline { get; }
        public IReadOnlyList<TranscriptChange> Changes { get; }
        public IReadOnlyList<TranscriptTurn> ChangedTurns => Changes
            .Where(change => change.Turn != null)
            .Select(change => change.Turn)
            .ToArray();
    }

    public sealed class TranscriptSubscriptionOptions
    {
        public bool ReplayExisting { get; set; }
        public bool IncludeActive { get; set; } = true;
        public bool IncludeTerminal { get; set; } = true;
        public TranscriptSpeakerType? SpeakerType { get; set; }
        public string SpeakerId { get; set; }
        public string ParticipantId { get; set; }

        public bool Matches(TranscriptTurn turn)
        {
            if (turn == null) return false;
            if (!IncludeActive && !turn.IsCommitted) return false;
            if (!IncludeTerminal && turn.IsCommitted) return false;
            if (SpeakerType.HasValue && turn.Speaker?.Type != SpeakerType.Value) return false;
            if (!string.IsNullOrWhiteSpace(SpeakerId) &&
                !string.Equals(turn.Speaker?.Id, SpeakerId, StringComparison.Ordinal))
                return false;
            if (!string.IsNullOrWhiteSpace(ParticipantId) &&
                !string.Equals(turn.Speaker?.ParticipantId, ParticipantId, StringComparison.Ordinal))
                return false;
            return true;
        }

        internal TranscriptSubscriptionOptions Copy() => new()
        {
            ReplayExisting = ReplayExisting,
            IncludeActive = IncludeActive,
            IncludeTerminal = IncludeTerminal,
            SpeakerType = SpeakerType,
            SpeakerId = SpeakerId,
            ParticipantId = ParticipantId
        };
    }

    public sealed class TranscriptCaptionSubscriptionOptions
    {
        public bool ReplayLatest { get; set; } = true;
        public bool IncludeStreaming { get; set; } = true;
        public bool IncludeFinal { get; set; } = true;
        public TranscriptSpeakerType? SpeakerType { get; set; }
        public string SpeakerId { get; set; }
        public string ParticipantId { get; set; }

        public bool Matches(TranscriptCaption caption)
        {
            if (caption == null || !caption.HasText) return false;
            if (!IncludeStreaming && !caption.IsFinal) return false;
            if (!IncludeFinal && caption.IsFinal) return false;
            if (SpeakerType.HasValue && caption.Speaker?.Type != SpeakerType.Value) return false;
            if (!string.IsNullOrWhiteSpace(SpeakerId) &&
                !string.Equals(caption.Speaker?.Id, SpeakerId, StringComparison.Ordinal))
                return false;
            if (!string.IsNullOrWhiteSpace(ParticipantId) &&
                !string.Equals(caption.Speaker?.ParticipantId, ParticipantId, StringComparison.Ordinal))
                return false;
            return true;
        }

        internal TranscriptCaptionSubscriptionOptions Copy() => new()
        {
            ReplayLatest = ReplayLatest,
            IncludeStreaming = IncludeStreaming,
            IncludeFinal = IncludeFinal,
            SpeakerType = SpeakerType,
            SpeakerId = SpeakerId,
            ParticipantId = ParticipantId
        };
    }

    internal static class TranscriptModelMapper
    {
        public static TranscriptTurn FromSnapshot(TranscriptTurnSnapshot snapshot)
        {
            if (snapshot == null) return null;

            TranscriptSpeaker speaker = TranscriptSpeaker.FromParticipant(snapshot.Participant);
            TranscriptSegment[] segments = snapshot.Segments
                .Select(segment => FromSnapshot(segment, speaker))
                .ToArray();

            TranscriptTextSource source = snapshot.PrimaryTextSource != TranscriptTextSource.Unknown
                ? snapshot.PrimaryTextSource
                : ResolvePrimarySource(snapshot, segments);

            return new TranscriptTurn(
                snapshot.TurnId,
                snapshot.MessageId,
                snapshot.ResponseId,
                snapshot.RoomSequence,
                snapshot.Revision,
                speaker,
                snapshot.State,
                source,
                snapshot.CommittedText,
                snapshot.InterimText,
                snapshot.DisplayText,
                snapshot.StartedAtUtc,
                snapshot.LastUpdatedAtUtc,
                snapshot.CompletedAtUtc,
                snapshot.WasInterrupted,
                segments);
        }

        private static TranscriptSegment FromSnapshot(TranscriptSegmentSnapshot snapshot, TranscriptSpeaker speaker)
        {
            TranscriptTextSource source = MapSource(snapshot.SourceKind, snapshot.Lifecycle, snapshot.InterimText);
            return new TranscriptSegment(
                snapshot.SegmentId,
                snapshot.TurnId,
                speaker,
                !string.IsNullOrWhiteSpace(snapshot.ProcessedOverrideText)
                    ? snapshot.ProcessedOverrideText
                    : snapshot.CommittedText,
                snapshot.InterimText,
                snapshot.DisplayText,
                MapState(snapshot.Lifecycle, false),
                source,
                snapshot.StartedAtUtc,
                snapshot.UpdatedAtUtc,
                snapshot.StoppedAtUtc);
        }

        private static TranscriptTextSource ResolvePrimarySource(
            TranscriptTurnSnapshot snapshot,
            IReadOnlyList<TranscriptSegment> segments)
        {
            TranscriptSegment segment = segments.LastOrDefault(s => !string.IsNullOrWhiteSpace(s.DisplayText));
            if (segment != null) return segment.Source;
            return snapshot.Participant.Kind == TranscriptParticipantKind.Character
                ? TranscriptTextSource.BotOutput
                : TranscriptTextSource.Unknown;
        }

        public static TranscriptTurnState MapState(TranscriptLifecycle lifecycle, bool wasInterrupted)
        {
            if (wasInterrupted) return TranscriptTurnState.Interrupted;
            return lifecycle switch
            {
                TranscriptLifecycle.Completed => TranscriptTurnState.Committed,
                TranscriptLifecycle.Stable => TranscriptTurnState.Stable,
                TranscriptLifecycle.Streaming => TranscriptTurnState.Streaming,
                _ => TranscriptTurnState.Streaming
            };
        }

        public static TranscriptTextSource MapSource(
            TranscriptSegmentSourceKind sourceKind,
            TranscriptLifecycle lifecycle,
            string interimText)
        {
            return sourceKind switch
            {
                TranscriptSegmentSourceKind.PlayerProcessedFinal => TranscriptTextSource.ProcessedFinal,
                TranscriptSegmentSourceKind.PlayerTypedText => TranscriptTextSource.TypedText,
                TranscriptSegmentSourceKind.BotOutput => TranscriptTextSource.BotOutput,
                // Snapshot-layer BotLlmPreview deliberately maps to public BotPreview.
                TranscriptSegmentSourceKind.BotLlmPreview => TranscriptTextSource.BotPreview,
                TranscriptSegmentSourceKind.LegacyBotTranscript => TranscriptTextSource.LegacyBotTranscript,
                TranscriptSegmentSourceKind.PlayerAsr => lifecycle == TranscriptLifecycle.Streaming
                    ? TranscriptTextSource.InterimAsr
                    : TranscriptTextSource.AsrFinal,
                TranscriptSegmentSourceKind.Unknown => TranscriptTextSource.Unknown,
                _ => TranscriptTextSource.Unknown
            };
        }
    }
}
