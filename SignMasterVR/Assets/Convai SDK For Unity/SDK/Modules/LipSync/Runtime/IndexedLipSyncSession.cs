using System;
using System.Collections.Generic;
using Convai.Domain.Models.LipSync;
using Convai.Infrastructure.Networking;

namespace Convai.Modules.LipSync
{
    internal enum IndexedLipSyncCompletionReason
    {
        None,
        AudioBoundary,
        StatsGrace,
        MissingAudioGate,
        SampleClockStall,
        CancelledTruncate,
        CancelledReset,
        Replaced
    }

    internal enum FutureChunkBufferResult
    {
        NotFuture,
        Buffered,
        DroppedRetired,
        DroppedInvalidOwner,
        DroppedCapacity,
        DroppedDuration
    }

    /// <summary>
    ///     Pure response-owned state for indexed lip sync. Keeps owner assembly, audio mapping,
    ///     bounded future responses, gate state, and completion watchdogs behind one reset path.
    /// </summary>
    internal sealed class IndexedLipSyncSession
    {
        private sealed class PendingResponse
        {
            public PendingResponse(LipSyncTimelineOwner owner, LinkedListNode<string> orderNode)
            {
                Owner = owner;
                OrderNode = orderNode;
            }

            public LipSyncTimelineOwner Owner { get; }
            public LinkedListNode<string> OrderNode { get; }
            public List<LipSyncPackedChunk> Chunks { get; } = new();
            public int FrameCount { get; set; }
        }

        private const int MaxFutureResponses = 3;
        private const double MaxFutureResponseSeconds = 3d;
        private readonly Dictionary<string, PendingResponse> _future = new(StringComparer.Ordinal);
        private readonly LinkedList<string> _futureOrder = new();
        private bool _hasIndexedStream;
        private bool _isClosing;
        private bool _gateOpen;

        public IndexedLipSyncSession(
            Func<AudioTimelineSnapshot?> readAudioTimeline,
            Func<double?> readLegacyPlayhead)
            : this(readAudioTimeline, null, readLegacyPlayhead)
        {
        }

        public IndexedLipSyncSession(
            Func<AudioTimelineSnapshot?> readAudioTimeline,
            Func<AudioMediaTimelineSnapshot?> readAudioMediaTimeline,
            Func<double?> readLegacyPlayhead)
        {
            Assembler = new LipSyncTimelineAssembler();
            Timeline = new ResponseAudioTimelineState(readAudioTimeline, readAudioMediaTimeline, readLegacyPlayhead);
            Reset(clearFutureResponses: true);
        }

        public LipSyncTimelineAssembler Assembler { get; }
        public ResponseAudioTimelineState Timeline { get; }
        public bool HasIndexedStream => _hasIndexedStream;
        public bool IsClosing => _isClosing;
        public bool GateOpen => _gateOpen;
        public float StatsGraceRemaining { get; set; }
        public float CompletionWatchdogRemaining { get; set; }
        public double CompletionLastTarget { get; set; }
        public double? ClosedInputBoundarySeconds { get; set; }
        public IndexedLipSyncCompletionReason CompletionReason { get; private set; }
        public bool TerminalSummaryLogged { get; private set; }

        /// <summary>Adopts a new active response and clears every response-scoped deadline.</summary>
        public void BeginActiveResponse()
        {
            _hasIndexedStream = true;
            _isClosing = false;
            _gateOpen = false;
            ClearCompletionState();
        }

        /// <summary>Marks an engine stream active without changing already-armed deadlines.</summary>
        public void MarkStreamActive()
        {
            _hasIndexedStream = true;
            _isClosing = false;
            _gateOpen = false;
        }

        public void OpenGate()
        {
            if (_hasIndexedStream) _gateOpen = true;
        }

        public void CloseGate() => _gateOpen = false;

        public void BeginClosing(IndexedLipSyncCompletionReason reason)
        {
            if (!_hasIndexedStream) return;

            _isClosing = true;
            CompletionReason = reason;
        }

        public void MarkTerminalSummaryLogged() => TerminalSummaryLogged = true;

        /// <summary>Clears active response state while preserving buffered future responses.</summary>
        public void RetireActiveResponse()
        {
            Timeline.ClearActiveAnchorMatch();
            _hasIndexedStream = false;
            _isClosing = false;
            _gateOpen = false;
            ClearCompletionState();
        }

        public void Reset(bool clearFutureResponses)
        {
            RetireActiveResponse();

            if (!clearFutureResponses) return;

            Timeline.Reset();
            Assembler.Reset();
            _future.Clear();
            _futureOrder.Clear();
        }

        public FutureChunkBufferResult TryBufferFutureChunk(
            LipSyncPackedChunk chunk,
            out string ownerKey,
            out string droppedOwnerKey)
        {
            ownerKey = null;
            droppedOwnerKey = null;
            if (!Assembler.HasActiveOwner) return FutureChunkBufferResult.NotFuture;

            var owner = new LipSyncTimelineOwner(chunk.ResponseId, chunk.NeuroSyncTurnId, chunk.Epoch);
            if (LipSyncTimelineAssembler.OwnersMatch(Assembler.ActiveOwner, owner))
                return FutureChunkBufferResult.NotFuture;
            if (Assembler.IsRetiredOwner(owner))
                return FutureChunkBufferResult.DroppedRetired;

            ownerKey = LipSyncTimelineAssembler.CanonicalOwnerKey(owner);
            if (ownerKey == null) return FutureChunkBufferResult.DroppedInvalidOwner;

            if (!_future.TryGetValue(ownerKey, out PendingResponse pending))
            {
                if (_future.Count >= MaxFutureResponses)
                {
                    LinkedListNode<string> oldest = _futureOrder.First;
                    if (oldest == null) return FutureChunkBufferResult.DroppedCapacity;
                    droppedOwnerKey = oldest.Value;
                    RemoveFuture(droppedOwnerKey);
                }

                LinkedListNode<string> orderNode = _futureOrder.AddLast(ownerKey);
                pending = new PendingResponse(owner, orderNode);
                _future.Add(ownerKey, pending);
            }

            int maxFrames = Math.Max(1, (int)Math.Ceiling(chunk.FrameRate * MaxFutureResponseSeconds));
            if (pending.FrameCount + chunk.FrameCount > maxFrames)
                return FutureChunkBufferResult.DroppedDuration;

            pending.Chunks.Add(chunk);
            pending.FrameCount += chunk.FrameCount;
            return droppedOwnerKey == null
                ? FutureChunkBufferResult.Buffered
                : FutureChunkBufferResult.DroppedCapacity;
        }

        public bool TryTakeFutureResponse(
            LipSyncTimelineOwner owner,
            out string ownerKey,
            out IReadOnlyList<LipSyncPackedChunk> chunks,
            out int frameCount)
        {
            ownerKey = owner.HasOwner ? LipSyncTimelineAssembler.CanonicalOwnerKey(owner) : _futureOrder.First?.Value;
            chunks = Array.Empty<LipSyncPackedChunk>();
            frameCount = 0;
            if (ownerKey == null || !_future.TryGetValue(ownerKey, out PendingResponse pending)) return false;

            chunks = pending.Chunks;
            frameCount = pending.FrameCount;
            RemoveFuture(ownerKey);
            return true;
        }

        private void RemoveFuture(string key)
        {
            if (!_future.TryGetValue(key, out PendingResponse pending)) return;
            _future.Remove(key);
            _futureOrder.Remove(pending.OrderNode);
        }

        private void ClearCompletionState()
        {
            StatsGraceRemaining = -1f;
            CompletionWatchdogRemaining = -1f;
            CompletionLastTarget = -1d;
            ClosedInputBoundarySeconds = null;
            CompletionReason = IndexedLipSyncCompletionReason.None;
            TerminalSummaryLogged = false;
        }
    }
}
