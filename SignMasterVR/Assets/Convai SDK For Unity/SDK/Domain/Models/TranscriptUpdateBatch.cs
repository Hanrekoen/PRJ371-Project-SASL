using System;
using System.Collections.Generic;

namespace Convai.Domain.Models
{
    /// <summary>
    ///     Incremental transcript update payload emitted by the room transcript engine.
    /// </summary>
    public sealed class TranscriptUpdateBatch
    {
        public TranscriptUpdateBatch(
            TranscriptTimelineSnapshot timeline,
            IReadOnlyList<TranscriptTurnSnapshot> changedTurns,
            IReadOnlyList<string> addedTurnIds,
            IReadOnlyList<string> updatedTurnIds,
            IReadOnlyList<string> completedTurnIds,
            IReadOnlyList<string> interruptedTurnIds,
            IReadOnlyList<string> correctedTurnIds,
            IReadOnlyList<string> removedTurnIds)
        {
            Timeline = timeline ?? TranscriptTimelineSnapshot.Empty;
            ChangedTurns = changedTurns ?? Array.Empty<TranscriptTurnSnapshot>();
            AddedTurnIds = addedTurnIds ?? Array.Empty<string>();
            UpdatedTurnIds = updatedTurnIds ?? Array.Empty<string>();
            CompletedTurnIds = completedTurnIds ?? Array.Empty<string>();
            InterruptedTurnIds = interruptedTurnIds ?? Array.Empty<string>();
            CorrectedTurnIds = correctedTurnIds ?? Array.Empty<string>();
            RemovedTurnIds = removedTurnIds ?? Array.Empty<string>();
        }

        public TranscriptTimelineSnapshot Timeline { get; }

        public long Cursor => Timeline.Cursor;

        public IReadOnlyList<TranscriptTurnSnapshot> ChangedTurns { get; }

        public IReadOnlyList<string> AddedTurnIds { get; }

        public IReadOnlyList<string> UpdatedTurnIds { get; }

        public IReadOnlyList<string> CompletedTurnIds { get; }

        public IReadOnlyList<string> InterruptedTurnIds { get; }

        public IReadOnlyList<string> CorrectedTurnIds { get; }

        public IReadOnlyList<string> RemovedTurnIds { get; }
    }
}
