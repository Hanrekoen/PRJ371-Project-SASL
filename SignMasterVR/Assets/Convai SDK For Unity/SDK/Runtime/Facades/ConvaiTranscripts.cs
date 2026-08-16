using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Convai.Application.Services.Transcript;
using Convai.Domain.Logging;
using Convai.Domain.Models;
using Convai.Runtime.Utilities;
using Newtonsoft.Json;

namespace Convai.Runtime.Facades
{
    /// <summary>
    ///     Unity-owned transcript timeline facade exposed from <c>ConvaiManager.Transcripts</c>.
    /// </summary>
    public sealed class ConvaiTranscripts : IDisposable
    {
        private readonly IRoomTranscriptEngine _engine;
        private readonly List<TranscriptSubscription> _subscriptions = new();
        private readonly List<CaptionSubscription> _captionSubscriptions = new();
        private readonly Dictionary<string, TranscriptCaption> _lastCaptionsByActor = new();
        private TranscriptTimelineSnapshot _lastMappedTimelineSnapshot;
        private TranscriptTimeline _cachedTimeline = TranscriptTimeline.Empty;
        private bool _disposed;

        internal ConvaiTranscripts(IRoomTranscriptEngine engine)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _engine.Changed += HandleEngineChanged;
            _engine.CaptionsChanged += HandleCaptionsChanged;
            foreach (TranscriptCaption caption in _engine.CurrentCaptions.Captions)
                _lastCaptionsByActor[CaptionKey(caption)] = caption;
        }

        public TranscriptTimeline CurrentTimeline
        {
            get
            {
                TranscriptTimelineSnapshot snapshot = _engine.CurrentTimeline;
                if (!ReferenceEquals(snapshot, _lastMappedTimelineSnapshot))
                {
                    TranscriptTimeline timeline = TranscriptTimeline.FromSnapshot(snapshot);
                    _lastMappedTimelineSnapshot = snapshot;
                    _cachedTimeline = timeline;
                }

                return _cachedTimeline;
            }
        }

        public TranscriptCaptionSnapshot CurrentCaptions => _engine.CurrentCaptions;

        /// <summary>
        ///     Whether shipped transcript presentation components should render transcript updates.
        ///     Canonical room history continues recording while presentation is disabled.
        /// </summary>
        public bool IsPresentationEnabled { get; private set; } = true;

        public event Action<TranscriptChangeBatch> Changed;
        public event Action<TranscriptTurn> TurnUpdated;
        public event Action<TranscriptTurn> TurnCommitted;
        public event Action<TranscriptTurn> TurnCorrected;
        public event Action<string> TurnRemoved;
        public event Action<TranscriptCaptionSnapshot> CaptionsChanged;
        public event Action<bool> PresentationEnabledChanged;

        public IReadOnlyList<TranscriptTurn> GetTurns(TranscriptQuery query = null)
        {
            return _engine.GetTurns(query)
                .Select(TranscriptModelMapper.FromSnapshot)
                .Where(turn => turn != null)
                .ToArray();
        }

        public TranscriptTurn GetTurn(string turnId) => TranscriptModelMapper.FromSnapshot(_engine.GetTurn(turnId));

        public TranscriptTurn GetLatestTurn(TranscriptParticipantRef participant) =>
            TranscriptModelMapper.FromSnapshot(_engine.GetLatestTurn(participant));

        public IDisposable Subscribe(
            Action<TranscriptChange> callback,
            TranscriptSubscriptionOptions options = null)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));

            TranscriptSubscriptionOptions effectiveOptions = (options ?? new TranscriptSubscriptionOptions()).Copy();
            var subscription = new TranscriptSubscription(this, callback, effectiveOptions);
            _subscriptions.Add(subscription);

            if (subscription.Options.ReplayExisting)
            {
                foreach (TranscriptTurn turn in CurrentTimeline.Turns.Where(subscription.Options.Matches))
                {
                    subscription.TryInvoke(new TranscriptChange(
                        turn.IsCommitted ? TranscriptChangeKind.Committed : TranscriptChangeKind.Added,
                        turn));
                }
            }

            return subscription;
        }

        public IDisposable SubscribeCommitted(
            Action<TranscriptChange> callback,
            TranscriptSubscriptionOptions options = null)
        {
            TranscriptSubscriptionOptions effectiveOptions = (options ?? new TranscriptSubscriptionOptions()).Copy();
            effectiveOptions.IncludeActive = false;
            effectiveOptions.IncludeTerminal = true;
            return Subscribe(callback, effectiveOptions);
        }

        /// <summary>
        ///     Subscribes to live speech-aligned captions. Use <see cref="Subscribe" /> for chat/history turns.
        /// </summary>
        public IDisposable SubscribeCaptions(
            Action<TranscriptCaption> callback,
            TranscriptCaptionSubscriptionOptions options = null)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));

            TranscriptCaptionSubscriptionOptions effectiveOptions =
                (options ?? new TranscriptCaptionSubscriptionOptions()).Copy();
            var subscription = new CaptionSubscription(this, callback, effectiveOptions);
            _captionSubscriptions.Add(subscription);

            if (effectiveOptions.ReplayLatest)
            {
                foreach (TranscriptCaption caption in CurrentCaptions.Captions.Where(effectiveOptions.Matches))
                    callback(caption);
            }

            return subscription;
        }

        public void Clear() => _engine.Clear();

        internal void SetPresentationEnabled(bool enabled)
        {
            if (_disposed || IsPresentationEnabled == enabled) return;

            IsPresentationEnabled = enabled;
            SafeEventInvoker.Invoke(
                PresentationEnabledChanged,
                enabled,
                null,
                "ConvaiTranscripts.PresentationEnabledChanged",
                LogCategory.Events);
        }

        public string Export(TranscriptExportFormat format)
        {
            TranscriptTurn[] turns = CurrentTimeline.CommittedTurns
                .OrderBy(turn => turn.RoomSequence)
                .ToArray();

            switch (format)
            {
                case TranscriptExportFormat.Json:
                    return JsonConvert.SerializeObject(turns, Formatting.Indented);

                case TranscriptExportFormat.Markdown:
                    return ExportMarkdown(turns);

                default:
                    return ExportPlainText(turns);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _engine.Changed -= HandleEngineChanged;
            _engine.CaptionsChanged -= HandleCaptionsChanged;
            _subscriptions.Clear();
            _captionSubscriptions.Clear();
            _lastCaptionsByActor.Clear();
        }

        private void HandleEngineChanged(TranscriptUpdateBatch batch)
        {
            TranscriptChangeBatch changeBatch = ToChangeBatch(batch);

            SafeEventInvoker.Invoke(Changed, changeBatch, null, "ConvaiTranscripts.Changed", LogCategory.Events);

            foreach (TranscriptChange change in changeBatch.Changes)
            {
                if (change.Kind == TranscriptChangeKind.Removed)
                {
                    SafeEventInvoker.Invoke(
                        TurnRemoved,
                        change.TurnId,
                        null,
                        "ConvaiTranscripts.TurnRemoved",
                        LogCategory.Events);
                    NotifySubscriptions(change);
                    continue;
                }

                if (change.Turn == null) continue;

                if (change.Kind == TranscriptChangeKind.Corrected)
                    SafeEventInvoker.Invoke(
                        TurnCorrected,
                        change.Turn,
                        null,
                        "ConvaiTranscripts.TurnCorrected",
                        LogCategory.Events);
                else if (IsCommitChange(change.Kind))
                    SafeEventInvoker.Invoke(
                        TurnCommitted,
                        change.Turn,
                        null,
                        "ConvaiTranscripts.TurnCommitted",
                        LogCategory.Events);
                else
                    SafeEventInvoker.Invoke(
                        TurnUpdated,
                        change.Turn,
                        null,
                        "ConvaiTranscripts.TurnUpdated",
                        LogCategory.Events);

                NotifySubscriptions(change);
            }
        }

        private TranscriptChangeBatch ToChangeBatch(TranscriptUpdateBatch batch)
        {
            TranscriptTimeline timeline = TranscriptTimeline.FromSnapshot(batch.Timeline);
            _lastMappedTimelineSnapshot = batch.Timeline;
            _cachedTimeline = timeline;
            var changes = new List<TranscriptChange>();
            var completedIds = new HashSet<string>(batch.CompletedTurnIds ?? Array.Empty<string>());
            var interruptedIds = new HashSet<string>(batch.InterruptedTurnIds ?? Array.Empty<string>());
            var correctedIds = new HashSet<string>(batch.CorrectedTurnIds ?? Array.Empty<string>());
            var addedIds = new HashSet<string>(batch.AddedTurnIds ?? Array.Empty<string>());

            foreach (TranscriptTurn turn in batch.ChangedTurns
                         .Select(TranscriptModelMapper.FromSnapshot)
                         .Where(turn => turn != null))
            {
                TranscriptChangeKind kind =
                    completedIds.Contains(turn.Id) ? TranscriptChangeKind.Committed :
                    interruptedIds.Contains(turn.Id) ? TranscriptChangeKind.Interrupted :
                    correctedIds.Contains(turn.Id) ? TranscriptChangeKind.Corrected :
                    addedIds.Contains(turn.Id) ? TranscriptChangeKind.Added :
                    TranscriptChangeKind.Updated;

                changes.Add(new TranscriptChange(kind, turn));
            }

            foreach (string removedTurnId in batch.RemovedTurnIds)
                changes.Add(new TranscriptChange(TranscriptChangeKind.Removed, null, removedTurnId));

            return new TranscriptChangeBatch(timeline, changes);
        }

        private void NotifySubscriptions(TranscriptChange change)
        {
            foreach (TranscriptSubscription subscription in _subscriptions.ToArray())
                subscription.TryInvoke(change);
        }

        private void RemoveSubscription(TranscriptSubscription subscription) => _subscriptions.Remove(subscription);

        private void HandleCaptionsChanged(TranscriptCaptionSnapshot snapshot)
        {
            if (snapshot == null) return;

            var currentKeys = new HashSet<string>(snapshot.Captions.Select(CaptionKey));
            foreach (string staleKey in _lastCaptionsByActor.Keys.Where(key => !currentKeys.Contains(key)).ToArray())
                _lastCaptionsByActor.Remove(staleKey);

            SafeEventInvoker.Invoke(
                CaptionsChanged,
                snapshot,
                null,
                "ConvaiTranscripts.CaptionsChanged",
                LogCategory.Events);

            foreach (TranscriptCaption caption in snapshot.Captions)
            {
                string key = CaptionKey(caption);
                if (_lastCaptionsByActor.TryGetValue(key, out TranscriptCaption previous) &&
                    string.Equals(previous.TurnId, caption.TurnId, StringComparison.Ordinal) &&
                    string.Equals(previous.Text, caption.Text, StringComparison.Ordinal) &&
                    previous.State == caption.State)
                    continue;

                _lastCaptionsByActor[key] = caption;
                foreach (CaptionSubscription subscription in _captionSubscriptions.ToArray())
                {
                    if (subscription.IsDisposed || !subscription.Options.Matches(caption)) continue;
                    subscription.Invoke(caption);
                }
            }
        }

        private void RemoveCaptionSubscription(CaptionSubscription subscription) =>
            _captionSubscriptions.Remove(subscription);

        private static string CaptionKey(TranscriptCaption caption) =>
            !string.IsNullOrWhiteSpace(caption.Speaker?.ParticipantId)
                ? $"{caption.Speaker?.Type}:participant:{caption.Speaker.ParticipantId}"
                : $"{caption.Speaker?.Type}:actor:{caption.Speaker?.Id}";

        private static bool IsCommitChange(TranscriptChangeKind kind) =>
            kind == TranscriptChangeKind.Committed || kind == TranscriptChangeKind.Interrupted;

        private static bool IsTerminalChange(TranscriptChangeKind kind) =>
            IsCommitChange(kind) || kind == TranscriptChangeKind.Corrected;

        private static string ExportPlainText(IEnumerable<TranscriptTurn> turns)
        {
            var builder = new StringBuilder();
            foreach (TranscriptTurn turn in turns)
            {
                string speaker = string.IsNullOrWhiteSpace(turn.Speaker?.DisplayName)
                    ? turn.Speaker?.Type.ToString() ?? "Unknown"
                    : turn.Speaker.DisplayName;
                builder.AppendLine($"{speaker}: {turn.DisplayText}");
            }

            return builder.ToString();
        }

        private static string ExportMarkdown(IEnumerable<TranscriptTurn> turns)
        {
            var builder = new StringBuilder();
            foreach (TranscriptTurn turn in turns)
            {
                string speaker = string.IsNullOrWhiteSpace(turn.Speaker?.DisplayName)
                    ? turn.Speaker?.Type.ToString() ?? "Unknown"
                    : turn.Speaker.DisplayName;
                builder.AppendLine($"**{speaker}:** {turn.DisplayText}");
                builder.AppendLine();
            }

            return builder.ToString();
        }

        private sealed class TranscriptSubscription : IDisposable
        {
            private readonly Action<TranscriptChange> _callback;
            private readonly HashSet<string> _matchedTurnIds = new();
            private readonly ConvaiTranscripts _owner;

            public TranscriptSubscription(
                ConvaiTranscripts owner,
                Action<TranscriptChange> callback,
                TranscriptSubscriptionOptions options)
            {
                _owner = owner;
                _callback = callback;
                Options = options;
                TerminalOnly = !options.IncludeActive && options.IncludeTerminal;
            }

            public TranscriptSubscriptionOptions Options { get; }
            public bool TerminalOnly { get; }
            public bool IsDisposed { get; private set; }

            public void TryInvoke(TranscriptChange change)
            {
                if (IsDisposed || change == null) return;

                if (change.Kind == TranscriptChangeKind.Removed)
                {
                    if (_matchedTurnIds.Remove(change.TurnId))
                        _callback(change);
                    return;
                }

                TranscriptTurn turn = change.Turn;
                if (!Options.Matches(turn)) return;
                if (TerminalOnly && !IsTerminalChange(change.Kind)) return;

                _matchedTurnIds.Add(turn.Id);
                _callback(change);
            }

            public void Dispose()
            {
                if (IsDisposed) return;
                IsDisposed = true;
                _matchedTurnIds.Clear();
                _owner.RemoveSubscription(this);
            }
        }

        private sealed class CaptionSubscription : IDisposable
        {
            private readonly Action<TranscriptCaption> _callback;
            private readonly ConvaiTranscripts _owner;

            public CaptionSubscription(
                ConvaiTranscripts owner,
                Action<TranscriptCaption> callback,
                TranscriptCaptionSubscriptionOptions options)
            {
                _owner = owner;
                _callback = callback;
                Options = options;
            }

            public TranscriptCaptionSubscriptionOptions Options { get; }
            public bool IsDisposed { get; private set; }

            public void Invoke(TranscriptCaption caption) => _callback(caption);

            public void Dispose()
            {
                if (IsDisposed) return;
                IsDisposed = true;
                _owner.RemoveCaptionSubscription(this);
            }
        }
    }
}
