using System;
using System.Collections.Generic;
using System.Diagnostics;
using Convai.Domain.DomainEvents.LipSync;
using Convai.Domain.Models.LipSync;
using Convai.Infrastructure.Networking;

namespace Convai.Modules.LipSync
{
    internal enum LipSyncTimelineAssemblerAction
    {
        None,
        EmitFrames,
        WaitingForGap,
        GapRecovered,
        OwnerChanged,
        DropStaleOwner,
        HardReset,

        /// <summary>
        ///     Cancel matched the active owner but the released tail (frames up to and including
        ///     <see cref="LipSyncTimelineAssemblerResult.ValidThroughFrameIndex" />) should keep playing.
        /// </summary>
        TruncateAfter
    }

    internal readonly struct LipSyncTimelineOwner
    {
        private readonly LipSyncResponseOwner _canonical;

        public LipSyncTimelineOwner(string responseId, int? neuroSyncTurnId, int? epoch)
        {
            _canonical = new LipSyncResponseOwner(responseId, neuroSyncTurnId, epoch);
            ResponseId = _canonical.ResponseId;
            NeuroSyncTurnId = neuroSyncTurnId;
            Epoch = epoch;
        }

        public string ResponseId { get; }
        public int? NeuroSyncTurnId { get; }
        public int? Epoch { get; }
        public bool HasOwner => _canonical.HasIdentity;
        internal string CanonicalKey => _canonical.CanonicalKey;
        internal bool Matches(in LipSyncTimelineOwner other) => _canonical.Matches(in other._canonical);
    }

    internal readonly struct LipSyncTimelineAssemblerResult
    {
        public LipSyncTimelineAssemblerResult(
            LipSyncTimelineAssemblerAction action,
            float[][] frames,
            IReadOnlyList<string> channelNames,
            float frameRate,
            LipSyncTimelineOwner owner,
            int firstFrameIndex = -1,
            int? validThroughFrameIndex = null)
        {
            Action = action;
            Frames = frames ?? Array.Empty<float[]>();
            ChannelNames = channelNames ?? Array.Empty<string>();
            FrameRate = frameRate;
            Owner = owner;
            FirstFrameIndex = firstFrameIndex;
            ValidThroughFrameIndex = validThroughFrameIndex;
        }

        public LipSyncTimelineAssemblerAction Action { get; }
        public float[][] Frames { get; }
        public int FrameCount => Frames.Length;
        public IReadOnlyList<string> ChannelNames { get; }
        public float FrameRate { get; }
        public LipSyncTimelineOwner Owner { get; }

        /// <summary>
        ///     Absolute timeline index of <c>Frames[0]</c>, or -1 when the chunk carried no
        ///     timeline metadata (append-order stream).
        /// </summary>
        public int FirstFrameIndex { get; }

        /// <summary>Set for <see cref="LipSyncTimelineAssemblerAction.TruncateAfter" /> results.</summary>
        public int? ValidThroughFrameIndex { get; }
    }

    internal sealed class LipSyncTimelineAssembler
    {
        private const int MaxRetiredOwners = 48;
        private const double GapTimeoutSeconds = 0.2d;
        private const double GapPlaybackDeadlineSeconds = 0.02d;
        private const double NeutralFadeSeconds = 0.1d;
        private const double MaxPendingSpanSeconds = 3d;
        private readonly SortedDictionary<int, float[]> _pending = new();
        private readonly HashSet<string> _retiredOwners = new(StringComparer.Ordinal);
        private readonly Queue<string> _retiredOwnerOrder = new();
        private readonly HashSet<int> _seenSequences = new();
        private readonly Func<double> _monotonicSeconds;

        private LipSyncTimelineOwner _activeOwner;
        private int _expectedFrameIndex;
        private IReadOnlyList<string> _channelNames = Array.Empty<string>();
        private float _frameRate = 60f;
        private double _gapStartedSeconds = -1d;
        private float[] _lastEmittedFrame;
        private int _recoveredGapCount;

        public LipSyncTimelineAssembler() : this(DefaultMonotonicSeconds)
        {
        }

        internal LipSyncTimelineAssembler(Func<double> monotonicSeconds)
        {
            _monotonicSeconds = monotonicSeconds ?? DefaultMonotonicSeconds;
        }

        public bool HasActiveOwner => _activeOwner.HasOwner;
        public LipSyncTimelineOwner ActiveOwner => _activeOwner;
        public int ExpectedFrameIndex => _expectedFrameIndex;
        public int PendingFrameCount => _pending.Count;
        public int RecoveredGapCount => _recoveredGapCount;
        public double GapAgeSeconds => _gapStartedSeconds < 0d
            ? 0d
            : Math.Max(0d, _monotonicSeconds() - _gapStartedSeconds);

        /// <summary>Frame rate declared by the active stream's chunks (60 before any chunk).</summary>
        public float ActiveFrameRate => _frameRate;

        public LipSyncTimelineAssemblerResult AddChunk(LipSyncPackedChunk chunk)
        {
            if (chunk == null || !chunk.IsValid)
                return Empty(LipSyncTimelineAssemblerAction.None);

            LipSyncTimelineOwner owner = FromChunk(chunk);
            if (owner.HasOwner && CanonicalOwnerKey(owner) == null)
                return Empty(LipSyncTimelineAssemblerAction.DropStaleOwner);
            if (owner.HasOwner && IsRetired(owner))
                return Empty(LipSyncTimelineAssemblerAction.DropStaleOwner);

            bool ownerChanged = false;
            if (owner.HasOwner)
            {
                if (!HasActiveOwner)
                {
                    AdoptOwner(owner, chunk);
                }
                else if (!MatchesActive(owner))
                {
                    Retire(_activeOwner);
                    AdoptOwner(owner, chunk);
                    ownerChanged = true;
                }
            }

            if (chunk.Sequence.HasValue && !_seenSequences.Add(chunk.Sequence.Value))
                return Empty(LipSyncTimelineAssemblerAction.DropStaleOwner);

            if (!chunk.HasTimelineMetadata)
            {
                if (chunk.FrameCount > 0) _lastEmittedFrame = chunk.Frames[chunk.FrameCount - 1];
                return new LipSyncTimelineAssemblerResult(
                    ownerChanged ? LipSyncTimelineAssemblerAction.OwnerChanged : LipSyncTimelineAssemblerAction.EmitFrames,
                    chunk.Frames,
                    chunk.ChannelNames,
                    chunk.FrameRate,
                    _activeOwner);
            }

            int start = chunk.StartFrameIndex.Value;
            for (int i = 0; i < chunk.FrameCount; i++)
            {
                int index = start + i;
                if (index < _expectedFrameIndex || _pending.ContainsKey(index)) continue;
                _pending.Add(index, chunk.Frames[i]);
            }

            int flushStartIndex = _expectedFrameIndex;
            float[][] emitted = FlushContiguous();
            if (emitted.Length > 0)
            {
                _gapStartedSeconds = -1d;
                return new LipSyncTimelineAssemblerResult(
                    ownerChanged ? LipSyncTimelineAssemblerAction.OwnerChanged : LipSyncTimelineAssemblerAction.EmitFrames,
                    emitted,
                    _channelNames,
                    _frameRate,
                    _activeOwner,
                    flushStartIndex);
            }

            if (ownerChanged)
                return Empty(LipSyncTimelineAssemblerAction.OwnerChanged);

            if (_gapStartedSeconds < 0d) _gapStartedSeconds = _monotonicSeconds();
            if (PendingSpanSeconds() >= MaxPendingSpanSeconds)
                return RecoverGap();

            return Empty(LipSyncTimelineAssemblerAction.WaitingForGap);
        }

        /// <summary>
        /// Resolves a permanently missing frame after the deterministic grace period. Missing
        /// indexes decay from the last emitted pose to neutral before later indexed data flushes.
        /// </summary>
        public LipSyncTimelineAssemblerResult ResolveExpiredGap(double? playbackSeconds = null)
        {
            if (_pending.Count == 0 || _gapStartedSeconds < 0d)
                return Empty(LipSyncTimelineAssemblerAction.None);

            double missingFrameSeconds = _expectedFrameIndex / (double)Math.Max(1f, _frameRate);
            bool playbackDeadlineReached = playbackSeconds.HasValue &&
                                           missingFrameSeconds - playbackSeconds.Value <=
                                           GapPlaybackDeadlineSeconds;
            if (!playbackDeadlineReached && GapAgeSeconds < GapTimeoutSeconds)
                return Empty(LipSyncTimelineAssemblerAction.None);

            return RecoverGap();
        }

        public LipSyncTimelineAssemblerResult CancelOwner(LipSyncTimelineResetRequested evt)
        {
            LipSyncTimelineOwner owner = FromReset(evt);
            if (!owner.HasOwner)
                return Empty(LipSyncTimelineAssemblerAction.None);

            if (!MatchesActive(owner))
            {
                Retire(owner);
                return Empty(LipSyncTimelineAssemblerAction.DropStaleOwner);
            }

            // A cancel with valid_through_frame_index means audio through that frame was already
            // released and should keep playing; only frames after it are discarded. The owner is
            // retired either way — the turn is over and late chunks must not be re-adopted.
            if (evt.ValidThroughFrameIndex.HasValue)
            {
                int validThrough = evt.ValidThroughFrameIndex.Value;
                LipSyncTimelineOwner cancelledOwner = _activeOwner;
                float frameRate = _frameRate;
                Retire(cancelledOwner);
                ClearActive();
                return new LipSyncTimelineAssemblerResult(
                    LipSyncTimelineAssemblerAction.TruncateAfter,
                    Array.Empty<float[]>(),
                    Array.Empty<string>(),
                    frameRate,
                    cancelledOwner,
                    -1,
                    validThrough);
            }

            Retire(owner);
            ClearActive();
            return Empty(LipSyncTimelineAssemblerAction.HardReset);
        }

        /// <summary>Whether the given identity refers to the currently active owner (alias-set match).</summary>
        public bool IsActiveOwner(string responseId, int? neuroSyncTurnId, int? epoch) =>
            MatchesActive(new LipSyncTimelineOwner(responseId, neuroSyncTurnId, epoch));

        internal bool IsRetiredOwner(LipSyncTimelineOwner owner) => IsRetired(owner);

        public void Reset()
        {
            ClearActive();
            _retiredOwners.Clear();
            _retiredOwnerOrder.Clear();
            _recoveredGapCount = 0;
        }

        public void CompleteActiveOwner()
        {
            if (HasActiveOwner) Retire(_activeOwner);
            ClearActive();
        }

        private void AdoptOwner(LipSyncTimelineOwner owner, LipSyncPackedChunk chunk)
        {
            _activeOwner = owner;
            _pending.Clear();
            _seenSequences.Clear();
            _expectedFrameIndex = 0;
            _channelNames = chunk.ChannelNames ?? Array.Empty<string>();
            _frameRate = Math.Max(1f, chunk.FrameRate);
            _gapStartedSeconds = -1d;
            _lastEmittedFrame = null;
        }

        private void ClearActive()
        {
            _activeOwner = default;
            _pending.Clear();
            _seenSequences.Clear();
            _expectedFrameIndex = 0;
            _channelNames = Array.Empty<string>();
            _frameRate = 60f;
            _gapStartedSeconds = -1d;
            _lastEmittedFrame = null;
        }

        private float[][] FlushContiguous()
        {
            var frames = new List<float[]>();
            while (_pending.TryGetValue(_expectedFrameIndex, out float[] frame))
            {
                frames.Add(frame);
                _lastEmittedFrame = frame;
                _pending.Remove(_expectedFrameIndex);
                _expectedFrameIndex++;
            }

            return frames.Count == 0 ? Array.Empty<float[]>() : frames.ToArray();
        }

        private bool MatchesActive(LipSyncTimelineOwner owner)
        {
            return HasActiveOwner && OwnersMatch(_activeOwner, owner);
        }

        private bool IsRetired(LipSyncTimelineOwner owner)
        {
            string key = CanonicalOwnerKey(owner);
            return key != null && _retiredOwners.Contains(key);
        }

        private void Retire(LipSyncTimelineOwner owner)
        {
            string key = CanonicalOwnerKey(owner);
            if (key == null || !_retiredOwners.Add(key)) return;

            _retiredOwnerOrder.Enqueue(key);
            while (_retiredOwnerOrder.Count > MaxRetiredOwners)
                _retiredOwners.Remove(_retiredOwnerOrder.Dequeue());
        }

        private LipSyncTimelineAssemblerResult RecoverGap()
        {
            int firstPendingIndex = -1;
            float[] firstPendingFrame = null;
            foreach (KeyValuePair<int, float[]> pair in _pending)
            {
                firstPendingIndex = pair.Key;
                firstPendingFrame = pair.Value;
                break;
            }

            int missingCount = firstPendingIndex - _expectedFrameIndex;
            if (missingCount <= 0)
            {
                _gapStartedSeconds = -1d;
                return Empty(LipSyncTimelineAssemblerAction.None);
            }

            int recoveryStart = _expectedFrameIndex;
            int channelCount = _lastEmittedFrame?.Length ?? firstPendingFrame?.Length ?? 0;
            var recovered = new List<float[]>(missingCount + _pending.Count);
            int fadeFrames = Math.Max(1, (int)Math.Ceiling(_frameRate * NeutralFadeSeconds));
            float[] sourceFrame = _lastEmittedFrame;
            for (int i = 0; i < missingCount; i++)
            {
                var frame = new float[channelCount];
                if (sourceFrame != null)
                {
                    float scale = Math.Max(0f, 1f - (i + 1f) / fadeFrames);
                    for (int channel = 0; channel < channelCount; channel++)
                        frame[channel] = sourceFrame[channel] * scale;
                }

                recovered.Add(frame);
                _lastEmittedFrame = frame;
                _expectedFrameIndex++;
            }

            float[][] future = FlushContiguous();
            for (int i = 0; i < future.Length; i++) recovered.Add(future[i]);

            _gapStartedSeconds = _pending.Count > 0 ? _monotonicSeconds() : -1d;
            _recoveredGapCount++;
            return new LipSyncTimelineAssemblerResult(
                LipSyncTimelineAssemblerAction.GapRecovered,
                recovered.ToArray(),
                _channelNames,
                _frameRate,
                _activeOwner,
                recoveryStart);
        }

        private double PendingSpanSeconds()
        {
            if (_pending.Count == 0 || _frameRate <= 0f) return 0d;

            int lastIndex = _expectedFrameIndex;
            foreach (int index in _pending.Keys) lastIndex = index;
            return Math.Max(0d, (lastIndex - _expectedFrameIndex + 1) / _frameRate);
        }

        internal static bool OwnersMatch(LipSyncTimelineOwner a, LipSyncTimelineOwner b)
        {
            return a.HasOwner && b.HasOwner && a.Matches(in b);
        }

        internal static string CanonicalOwnerKey(LipSyncTimelineOwner owner)
        {
            return string.IsNullOrEmpty(owner.CanonicalKey) ? null : owner.CanonicalKey;
        }

        private static double DefaultMonotonicSeconds() =>
            Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

        private LipSyncTimelineAssemblerResult Empty(LipSyncTimelineAssemblerAction action) =>
            new(action, Array.Empty<float[]>(), _channelNames, _frameRate, _activeOwner);

        private static LipSyncTimelineOwner FromChunk(LipSyncPackedChunk chunk) =>
            new(chunk.ResponseId, chunk.NeuroSyncTurnId, chunk.Epoch);

        private static LipSyncTimelineOwner FromReset(LipSyncTimelineResetRequested evt) =>
            new(evt.ResponseId, evt.NeuroSyncTurnId, evt.Epoch);
    }
}
