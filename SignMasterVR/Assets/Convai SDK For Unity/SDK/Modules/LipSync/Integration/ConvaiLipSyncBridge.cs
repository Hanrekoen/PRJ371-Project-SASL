using System;
using Convai.Domain.DomainEvents.LipSync;
using Convai.Domain.DomainEvents.Runtime;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Domain.Models.LipSync;
using Convai.Runtime.Logging;
using Convai.Runtime.Room;
using Convai.Infrastructure.Networking;
using ILogger = Convai.Domain.Logging.ILogger;

namespace Convai.Modules.LipSync
{
    /// <summary>
    ///     Bridges Convai SDK events (IEventHub) to the LipSync playback engine.
    ///     Subscribes to LipSyncPackedDataReceived, CharacterSpeechStateChanged, and CharacterAudioPlaybackStateChanged.
    ///     Filters by character, feeds frames to the engine, gates playback start on actual audio signal,
    ///     and starts fade-out when remote audio playback stops so buffered lip sync does not trail silence.
    /// </summary>
    internal sealed class ConvaiLipSyncBridge : IDisposable
    {
        private const string LogPrefix = "[Convai LipSync Bridge]";
        private const double CompletionTailSeconds = 0.25d;
        private const float CompletionStallTimeoutSeconds = 0.5f;

        /// <summary>How long to wait after owner-matched turn stats with a frame-count mismatch before force-ending.</summary>
        private const float StatsMismatchGraceSeconds = 0.4f;

        private readonly LipSyncPlaybackEngine _engine;
        private readonly LipSyncProfileId _lockedProfile;
        private readonly ILogger _logger;
        private readonly Action _playbackGateClosed;
        private readonly Action _playbackGateOpened;
        private readonly Action _playbackGatePaused;
        private readonly Action _playbackGateResumed;
        private readonly IConvaiRoomAudioService _roomAudioService;
        private readonly IndexedLipSyncSession _session;
        private SubscriptionToken _audioPlaybackToken;
        private string _characterId;
        private SubscriptionToken _dataToken;
        private bool _disposed;
        private SubscriptionToken _anchorToken;
        private SubscriptionToken _sampleAnchorToken;


        private IEventHub _eventHub;
        private bool _isAudioPlaybackActiveForCharacter;
        private bool _isCharacterSpeaking;
        private DateTime? _lastAudioPlaybackStartedUtc;
        private DateTime? _lastAudioPlaybackStoppedUtc;
        private bool _lastLifecycleSpeaking;
        private DateTime _lastLifecycleTimestamp;
        private SubscriptionToken _timelineResetToken;
        private SubscriptionToken _turnStatsToken;
        private SubscriptionToken _speechToken;
        private SubscriptionToken _responseLifecycleToken;

        public ConvaiLipSyncBridge(
            LipSyncPlaybackEngine engine,
            LipSyncProfileId lockedProfile,
            IConvaiRoomAudioService roomAudioService = null,
            ILogger logger = null,
            Action playbackGateOpened = null,
            Action playbackGateClosed = null,
            Action playbackGatePaused = null,
            Action playbackGateResumed = null)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _lockedProfile = lockedProfile;
            _roomAudioService = roomAudioService;
            _logger = logger;
            _playbackGateOpened = playbackGateOpened;
            _playbackGateClosed = playbackGateClosed;
            _playbackGatePaused = playbackGatePaused;
            _playbackGateResumed = playbackGateResumed;
            _session = new IndexedLipSyncSession(
                TryReadAudioTimelineSnapshot,
                TryReadAudioMediaTimelineSnapshot,
                TryReadRawPlayhead);
        }

        /// <summary>
        ///     Device-measured playhead for the bound character (seconds of source audio rendered
        ///     since the current playback signal started), or null when the platform exposes none.
        /// </summary>
        private double? TryReadRawPlayhead()
        {
            if (_roomAudioService != null &&
                _roomAudioService.TryGetCharacterAudioPlayhead(_characterId, out double played))
                return played;

            return null;
        }

        private AudioTimelineSnapshot? TryReadAudioTimelineSnapshot()
        {
            if (_roomAudioService is IConvaiRoomAudioTimelineService timelineService &&
                timelineService.TryGetCharacterAudioTimeline(_characterId, out AudioTimelineSnapshot snapshot))
                return snapshot;

            return null;
        }

        private AudioMediaTimelineSnapshot? TryReadAudioMediaTimelineSnapshot()
        {
            if (_roomAudioService is IConvaiRoomAudioMediaTimelineService timelineService &&
                timelineService.TryGetCharacterAudioMediaTimeline(
                    _characterId,
                    out AudioMediaTimelineSnapshot snapshot))
                return snapshot;

            return null;
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            Unbind();
        }

        /// <summary>
        ///     Binds to the event hub and subscribes to lip sync data, speech state, and audio playback events.
        ///     Playback starts only after <see cref="CharacterAudioPlaybackStateChanged" /> reports audible playback.
        ///     Speech state still marks stream end, but does not gate onset because server speech state can lag WebGL
        ///     HTML audio playback and push lip sync behind audible speech.
        /// </summary>
        public void Bind(IEventHub eventHub, string characterId)
        {
            Unbind();
            _eventHub = eventHub;
            _characterId = characterId?.Trim() ?? string.Empty;
            _isAudioPlaybackActiveForCharacter = false;
            _isCharacterSpeaking = false;
            _lastAudioPlaybackStartedUtc = null;
            _lastAudioPlaybackStoppedUtc = null;
            _lastLifecycleTimestamp = default;
            _lastLifecycleSpeaking = false;
            _session.Reset(clearFutureResponses: true);

            if (_eventHub == null || string.IsNullOrWhiteSpace(_characterId))
            {
                if (CanLogDebug)
                    LogDebug($"Bind skipped: eventHubNull={_eventHub == null}, characterId='{_characterId}'.");
                return;
            }

            _dataToken = _eventHub.Subscribe<LipSyncPackedDataReceived>(OnPackedDataReceived);
            _responseLifecycleToken = _eventHub.Subscribe<LipSyncResponseLifecycleChanged>(OnResponseLifecycleChanged);
            _speechToken = _eventHub.Subscribe<CharacterSpeechStateChanged>(OnSpeechStateChanged);
            _audioPlaybackToken = _eventHub.Subscribe<CharacterAudioPlaybackStateChanged>(OnAudioPlaybackStateChanged);
            _turnStatsToken = _eventHub.Subscribe<BlendshapeTurnStatsReceived>(OnBlendshapeTurnStatsReceived);
            _timelineResetToken = _eventHub.Subscribe<LipSyncTimelineResetRequested>(OnTimelineResetRequested);
            _anchorToken = _eventHub.Subscribe<LipSyncAudioTimelineAnchorReceived>(OnAudioTimelineAnchorReceived);
            _sampleAnchorToken = _eventHub.Subscribe<AudioTimelineSampleAnchor>(OnAudioTimelineSampleAnchor);
            RestoreExistingBrowserPlaybackState();
            if (CanLogDebug)
                LogDebug($"Bound: characterId='{_characterId}', profile='{_lockedProfile}'.");
        }

        private void RestoreExistingBrowserPlaybackState()
        {
            AudioMediaTimelineSnapshot? snapshot = TryReadAudioMediaTimelineSnapshot();
            if (!snapshot.HasValue || snapshot.Value.State != AudioTimelinePlaybackState.Playing)
                return;

            // A WebGL media element can already be playing when this character module binds.
            // Its one immediate PlaybackStarted callback may therefore have been published before
            // the subscriptions above existed. Reconcile from the per-character media source;
            // the room-wide IsAudioPlaybackActive flag is only browser permission state. An
            // underrun is not sufficient because waiting/stalled can precede the first audible
            // sample; the element will publish playing after this bridge is bound when it resumes.
            DateTime timestamp = DateTime.UtcNow;
            _isAudioPlaybackActiveForCharacter = true;
            _lastAudioPlaybackStartedUtc = timestamp;
            _session.Timeline.RecordAudioStart(timestamp);
            if (CanLogDebug)
                LogDebug("Recovered existing per-character browser playback during Bind.");
        }

        public void Unbind()
        {
            if (!string.IsNullOrWhiteSpace(_characterId))
            {
                if (CanLogDebug)
                    LogDebug(
                        $"Unbind: characterId='{_characterId}', state='{_engine.State}', indexed={_session.HasIndexedStream}.");
            }

            if (_eventHub != null)
            {
                if (_dataToken != default)
                {
                    _eventHub.Unsubscribe(_dataToken);
                    _dataToken = default;
                }

                if (_speechToken != default)
                {
                    _eventHub.Unsubscribe(_speechToken);
                    _speechToken = default;
                }

                if (_responseLifecycleToken != default)
                {
                    _eventHub.Unsubscribe(_responseLifecycleToken);
                    _responseLifecycleToken = default;
                }

                if (_audioPlaybackToken != default)
                {
                    _eventHub.Unsubscribe(_audioPlaybackToken);
                    _audioPlaybackToken = default;
                }

                if (_turnStatsToken != default)
                {
                    _eventHub.Unsubscribe(_turnStatsToken);
                    _turnStatsToken = default;
                }

                if (_timelineResetToken != default)
                {
                    _eventHub.Unsubscribe(_timelineResetToken);
                    _timelineResetToken = default;
                }

                if (_anchorToken != default)
                {
                    _eventHub.Unsubscribe(_anchorToken);
                    _anchorToken = default;
                }

                if (_sampleAnchorToken != default)
                {
                    _eventHub.Unsubscribe(_sampleAnchorToken);
                    _sampleAnchorToken = default;
                }
            }

            _eventHub = null;
            _characterId = string.Empty;
            _isAudioPlaybackActiveForCharacter = false;
            _isCharacterSpeaking = false;
            _lastAudioPlaybackStartedUtc = null;
            _lastAudioPlaybackStoppedUtc = null;
            _lastLifecycleTimestamp = default;
            _lastLifecycleSpeaking = false;
            _session.Reset(clearFutureResponses: true);
        }

        /// <summary>
        ///     Advances bridge-side timers. Called once per frame by the runtime controller.
        ///     Ends the indexed stream when the stats-mismatch grace window expires without the
        ///     missing frames arriving (Web SDK "grace window" pattern — a lost trailing chunk must
        ///     not leave the stream open forever).
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (_disposed) return;

            // Run an already-armed completion watchdog before stats can arm a new one. This keeps
            // the full no-progress timeout instead of charging the stats grace tick twice.
            TickCompletionWatchdog(deltaTime);
            TickStatsGrace(deltaTime);
            TickGapRecovery();
            TickAuthoritativeClockGate();
            CleanupCompletedIndexedStream();
        }

        private void TickCompletionWatchdog(float deltaTime)
        {
            if (_session.CompletionWatchdogRemaining < 0f || !_session.HasIndexedStream) return;

            // A response that never opened its audible gate must not treat later silent/zero PCM
            // progress as permission to start. Give cross-channel ordering a bounded chance to
            // deliver a real audio-start event, then discard the still-invisible response.
            if (!_session.GateOpen && !_isAudioPlaybackActiveForCharacter)
            {
                _session.CompletionWatchdogRemaining -= Math.Max(0f, deltaTime);
                if (_session.CompletionWatchdogRemaining >= 0f) return;

                ExpireCompletionWatchdog(
                    IndexedLipSyncCompletionReason.MissingAudioGate,
                    "audio never opened the response gate; discarded buffered animation without playback");
                return;
            }

            if (TryGetAudioTimelineTarget(out double target))
            {
                if (_session.ClosedInputBoundarySeconds.HasValue &&
                    target >= _session.ClosedInputBoundarySeconds.Value)
                {
                    _session.CompletionWatchdogRemaining = -1f;
                    _session.ClosedInputBoundarySeconds = null;
                    _session.BeginClosing(IndexedLipSyncCompletionReason.AudioBoundary);
                    _engine.NotifyStreamEnd();
                    return;
                }

                if (target > _session.CompletionLastTarget + 1e-6d)
                {
                    _session.CompletionLastTarget = target;
                    _session.CompletionWatchdogRemaining = CompletionStallTimeoutSeconds;
                    return;
                }
            }

            _session.CompletionWatchdogRemaining -= Math.Max(0f, deltaTime);
            if (_session.CompletionWatchdogRemaining >= 0f) return;

            ExpireCompletionWatchdog(
                IndexedLipSyncCompletionReason.SampleClockStall,
                "sample clock stopped progressing; faded visual stream at audio position");
        }

        private void ExpireCompletionWatchdog(IndexedLipSyncCompletionReason completionReason, string warning)
        {
            _session.CompletionWatchdogRemaining = -1f;
            _session.ClosedInputBoundarySeconds = null;
            _session.BeginClosing(completionReason);
            _session.StatsGraceRemaining = -1f;
            _engine.StopSmooth();
            if (CanLogWarning)
                LogWarning($"Completion recovery: {warning}. {SyncTimingSummary(DateTime.UtcNow)}");
        }

        private void CleanupCompletedIndexedStream()
        {
            if (!_session.HasIndexedStream || !_session.IsClosing || _engine.State != PlaybackState.Idle) return;

            LogResponseSummary(_session.CompletionReason);
            _session.Assembler.CompleteActiveOwner();
            _session.RetireActiveResponse();
            PromoteFutureResponse(default);
        }

        private void TickGapRecovery()
        {
            double? playbackSeconds = TryGetAudioTimelineTarget(out double targetSeconds)
                ? targetSeconds
                : null;
            LipSyncTimelineAssemblerResult result = _session.Assembler.ResolveExpiredGap(playbackSeconds);
            if (result.Action != LipSyncTimelineAssemblerAction.GapRecovered || result.FrameCount == 0) return;

            if (!_session.HasIndexedStream) _session.MarkStreamActive();
            if (_engine.State == PlaybackState.Idle)
            {
                _engine.BeginStream(result.ChannelNames, result.FrameRate, sendAheadTimeline: true);
                _session.MarkStreamActive();
            }

            double timelineStart = result.FirstFrameIndex / (double)Math.Max(1f, result.FrameRate);
            _engine.FeedFramesAt(result.Frames, timelineStart);
            if (CanLogWarning)
                LogWarning(
                    $"Recovered indexed gap: firstIndex={result.FirstFrameIndex}, frames={result.FrameCount}, recoveredTotal={_session.Assembler.RecoveredGapCount}.");
            if (LipSyncDriftMonitor.Enabled)
                LipSyncDriftMonitor.RecordEvent(_characterId,
                    $"gap recovered @{result.FirstFrameIndex}, frames={result.FrameCount}");
        }

        private void TickAuthoritativeClockGate()
        {
            if (!_session.HasIndexedStream || _session.GateOpen || _engine.State != PlaybackState.Buffering) return;

            // Polling advances the bounded browser-onset wait. Exact analyser onset wins; if Web
            // Audio is unavailable, the lifecycle media baseline becomes authoritative after 500 ms.
            int fallbackCountBefore = _session.Timeline.BrowserFallbackCount;
            _session.Timeline.TryGetTarget(out _);
            if (_session.Timeline.BrowserFallbackCount > fallbackCountBefore)
            {
                LogDebug("browser_signal_timeout: using lifecycle browser-media baseline.");
                if (LipSyncDriftMonitor.Enabled)
                    LipSyncDriftMonitor.RecordEvent(_characterId, "browser_signal_timeout");
            }
            if (!ShouldOpenPlaybackGate(true)) return;

            ResumePlaybackGate();
        }

        private void TickStatsGrace(float deltaTime)
        {
            if (_session.StatsGraceRemaining < 0f) return;

            _session.StatsGraceRemaining -= Math.Max(0f, deltaTime);
            if (_session.StatsGraceRemaining >= 0f) return;

            if (!_session.HasIndexedStream) return;

            if (!_session.GateOpen && !_isAudioPlaybackActiveForCharacter)
            {
                _session.StatsGraceRemaining = -1f;
                _session.CompletionWatchdogRemaining = CompletionStallTimeoutSeconds;
                if (CanLogDebug)
                    LogDebug(
                        $"TurnStats closed a response whose audible gate never opened; armed discard watchdog. {SyncTimingSummary(DateTime.UtcNow)}");
                return;
            }

            double completionBoundary = _session.Timeline.TryGetFinalTimelineSeconds(out double finalSampleSeconds)
                ? finalSampleSeconds
                : _engine.TotalIngressDuration;
            if (_session.Timeline.IsAuthoritativeClockBound &&
                TryGetAudioTimelineTarget(out double targetSeconds) &&
                targetSeconds < completionBoundary + CompletionTailSeconds)
            {
                // Input is closed, but the rendered-audio clock may still be advancing toward the
                // final indexed frame. Keep sampling while it advances. If it stalls before that
                // boundary, audio remains authoritative: fade the current pose instead of waiting
                // forever for a visual timestamp the audio clock can no longer reach.
                _session.StatsGraceRemaining = -1f;
                _session.CompletionLastTarget = targetSeconds;
                _session.CompletionWatchdogRemaining = CompletionStallTimeoutSeconds;
                _session.ClosedInputBoundarySeconds = completionBoundary + CompletionTailSeconds;
                LogDebug("TurnStats closed input before animation boundary: armed sample-progress watchdog.");
                return;
            }

            _session.StatsGraceRemaining = -1f;
            _session.ClosedInputBoundarySeconds = null;

            LogDebug("TurnStats grace window expired without missing frames: notifying stream end.");
            if (LipSyncDriftMonitor.Enabled) LipSyncDriftMonitor.RecordEvent(_characterId, "stats grace expired: end");
            _session.BeginClosing(IndexedLipSyncCompletionReason.StatsGrace);
            _engine.NotifyStreamEnd();
        }

        /// <summary>
        ///     Current position on the active turn's audio timeline in seconds, measured from the
        ///     audio actually rendered to the device. Valid only while an indexed stream is active
        ///     and audio is audible; the runtime controller uses it to start the visual clock at the
        ///     right offset and to correct drift continuously.
        /// </summary>
        public bool TryGetAudioTimelineTarget(out double targetSeconds)
        {
            targetSeconds = 0d;
            if (_disposed || !_session.HasIndexedStream)
                return false;

            if (!_session.Timeline.IsAuthoritativeClockBound && !_isAudioPlaybackActiveForCharacter)
                return false;

            return _session.Timeline.TryGetTarget(out targetSeconds);
        }

        internal bool TryGetSampleLockedTarget(out double targetSeconds)
        {
            targetSeconds = 0d;
            return _session.Timeline.IsSampleClockBound && TryGetAudioTimelineTarget(out targetSeconds);
        }

        internal bool TryGetAuthoritativeTimelineTarget(out double targetSeconds)
        {
            targetSeconds = 0d;
            return _session.Timeline.IsAuthoritativeClockBound &&
                   TryGetAudioTimelineTarget(out targetSeconds);
        }

        internal bool ConsumeAuthoritativeClockDiscontinuity() =>
            _session.Timeline.ConsumeAuthoritativeDiscontinuity();

        internal bool ConsumeSampleClockDiscontinuity() =>
            _session.Timeline.ConsumeSampleDiscontinuity();

        /// <summary>Initial clock elapsed for a stream whose audio started before the gate opened.</summary>
        public double GetPlaybackStartElapsedSeconds() =>
            TryGetAudioTimelineTarget(out double target) ? target : 0d;

        private void OnAudioTimelineAnchorReceived(LipSyncAudioTimelineAnchorReceived evt)
        {
            if (_disposed || _eventHub == null) return;
            if (!IsForThisCharacter(evt.CharacterId)) return;
            if (!evt.IsValid) return;

            bool matchesActiveOwner = _session.Assembler.HasActiveOwner &&
                                      _session.Assembler.IsActiveOwner(evt.ResponseId, evt.NeuroSyncTurnId, evt.Epoch);
            AnchorRegistration result = _session.Timeline.RegisterAnchor(
                new LipSyncTimelineOwner(evt.ResponseId, evt.NeuroSyncTurnId, evt.Epoch),
                evt.AudioStartMs / 1000d,
                matchesActiveOwner,
                evt.Sequence);

            switch (result)
            {
                case AnchorRegistration.MatchedActiveOwner:
                    if (CanLogDebug)
                        LogDebug(
                            $"Anchor matched active owner: response='{evt.ResponseId}', audioStartMs={evt.AudioStartMs:F1}, timelineBase={_session.Timeline.TimelineBaseSeconds:F3}s.");
                    if (LipSyncDriftMonitor.Enabled)
                        LipSyncDriftMonitor.RecordEvent(_characterId, $"anchor matched @{evt.AudioStartMs:F0}ms");

                    // A first matching anchor may improve mapping; it never blocks playback.
                    if (_session.HasIndexedStream && ShouldOpenPlaybackGate(true))
                        ResumePlaybackGate();
                    break;
                case AnchorRegistration.BufferedForFutureOwner:
                    if (CanLogDebug)
                        LogDebug(
                            $"Anchor buffered for future owner: response='{evt.ResponseId}', audioStartMs={evt.AudioStartMs:F1}.");
                    break;
                case AnchorRegistration.Stale:
                    if (CanLogDebug)
                        LogDebug($"Anchor ignored: non-monotonic sequence for response '{evt.ResponseId}'.");
                    break;
            }
        }

        private void OnAudioTimelineSampleAnchor(AudioTimelineSampleAnchor evt)
        {
            if (_disposed || _eventHub == null || !evt.IsValid || !IsForThisCharacter(evt.CharacterId)) return;

            var owner = new LipSyncTimelineOwner(evt.ResponseId, evt.TurnId, evt.Epoch);
            bool matchesActiveOwner = _session.Assembler.HasActiveOwner &&
                                      _session.Assembler.IsActiveOwner(evt.ResponseId, evt.TurnId, evt.Epoch);
            SampleAnchorRegistration registration = _session.Timeline.RegisterSampleAnchor(
                owner,
                evt.SampleRate,
                evt.ResponseAudioStartSample,
                evt.FinalAudioSample,
                matchesActiveOwner);
            if (registration == SampleAnchorRegistration.InvalidBounds && CanLogWarning)
            {
                LogWarning(
                    $"Rejected sample anchor with invalid bounds: response='{evt.ResponseId}', start={evt.ResponseAudioStartSample}, final={evt.FinalAudioSample?.ToString() ?? "null"}, rate={evt.SampleRate}.");
            }
        }

        private void OnAudioPlaybackStateChanged(CharacterAudioPlaybackStateChanged evt)
        {
            if (_disposed || _eventHub == null) return;

            if (!IsForThisCharacter(evt.CharacterId)) return;

            if (evt.IsPlaying)
            {
                _lastAudioPlaybackStartedUtc = evt.Timestamp;
                _session.Timeline.RecordAudioStart(evt.Timestamp);
                if (LipSyncDriftMonitor.Enabled)
                    LipSyncDriftMonitor.RecordEvent(_characterId,
                        $"audio start (base={_session.Timeline.TimelineBaseSeconds:F2}s)");
            }
            else
            {
                _lastAudioPlaybackStoppedUtc = evt.Timestamp;
                _session.Timeline.RecordAudioStop();
                if (LipSyncDriftMonitor.Enabled)
                    LipSyncDriftMonitor.RecordEvent(_characterId,
                        $"audio stop (frozen @{_session.Timeline.TimelineBaseSeconds:F2}s)");
            }

            _isAudioPlaybackActiveForCharacter = evt.IsPlaying;
            if (CanLogDebug)
                LogDebug(
                    $"AudioPlayback: isPlaying={evt.IsPlaying}, indexed={_session.HasIndexedStream}, speaking={_isCharacterSpeaking}, gate={ShouldOpenPlaybackGate(_session.HasIndexedStream)}, state='{_engine.State}', buffered={_engine.BufferedDuration:F3}s, timelineBase={_session.Timeline.TimelineBaseSeconds:F3}s, {SyncTimingSummary(evt.Timestamp)}.");
            if (!evt.IsPlaying)
            {
                if (_session.HasIndexedStream)
                {
                    bool completionPending = _session.StatsGraceRemaining >= 0f ||
                                             _session.CompletionWatchdogRemaining >= 0f ||
                                             _session.IsClosing;
                    if (completionPending)
                    {
                        // Once terminal lifecycle evidence has arrived, audio stop is the audible
                        // boundary. Fade the last sampled pose now instead of holding it through
                        // the late-chunk/watchdog windows. A quick playback restart can still
                        // cancel this reversible fade in NotifyAudioPlaybackStarted.
                        LogDebug("AudioPlayback stopped with indexed completion pending: starting smooth stop.");
                        _engine.StopSmooth();
                        return;
                    }

                    if (_session.Timeline.IsAuthoritativeClockBound)
                    {
                        LogDebug("Playback stop left the indexed stream on its authoritative audio clock.");
                        return;
                    }

                    LogDebug("AudioPlayback stopped while indexed stream active: pausing gate without fade.");
                    _session.CloseGate();
                    _playbackGatePaused?.Invoke();
                    return;
                }

                LogDebug("AudioPlayback stopped: closing gate and starting smooth stop.");
                _playbackGateClosed?.Invoke();
                _engine.NotifyAudioPlaybackStopped();
                return;
            }

            if (!ShouldOpenPlaybackGate(_session.HasIndexedStream))
            {
                LogDebug("AudioPlayback start did not open gate: gate predicate false.");
                return;
            }

            // No stream yet (e.g. residual audio tail after an interrupted turn, or audio ahead of
            // the first chunk): opening the gate now would start the clock with nothing to play.
            // The gate is re-applied when the first chunk begins a stream.
            if (_engine.State == PlaybackState.Idle)
            {
                LogDebug("AudioPlayback start noted while engine idle: gate deferred until a stream begins.");
                return;
            }

            if (_session.HasIndexedStream)
                ResumePlaybackGate();
            else
                OpenPlaybackGate();
        }

        private void OnPackedDataReceived(LipSyncPackedDataReceived evt)
        {
            if (_disposed || _eventHub == null) return;

            if (!evt.IsValid)
            {
                LogDebug("Packet ignored: event invalid.");
                return;
            }

            if (!IsForThisCharacter(evt.CharacterId))
            {
                if (CanLogDebug)
                    LogDebug($"Packet ignored: event character '{evt.CharacterId}' != bound '{_characterId}'.");
                return;
            }

            if (evt.ProfileId != _lockedProfile)
            {
                if (CanLogDebug)
                    LogDebug($"Packet dropped: profile '{evt.ProfileId}' != locked '{_lockedProfile}'.");
                return;
            }

            LipSyncPackedChunk chunk = evt.Chunk;
            if (chunk.FrameCount <= 0)
            {
                if (CanLogDebug)
                    LogDebug($"Packet ignored: frameCount={chunk.FrameCount}, {ChunkSummary(chunk)}.");
                return;
            }

            if (CanLogDebug)
                LogDebug(
                    $"Packet: frames={chunk.FrameCount}, {ChunkSummary(chunk)}, engine='{_engine.State}', indexedActive={_session.HasIndexedStream}, audio={_isAudioPlaybackActiveForCharacter}, speaking={_isCharacterSpeaking}, buffered={_engine.BufferedDuration:F3}s, {SyncTimingSummary(evt.Timestamp)}.");

            if (chunk.HasOwnerMetadata || chunk.HasTimelineMetadata)
            {
                FeedIndexedChunk(chunk);
                return;
            }

            if (_session.HasIndexedStream)
            {
                LogResponseSummary(IndexedLipSyncCompletionReason.Replaced);
                LogDebug("Switching from indexed stream to legacy stream: resetting indexed response state.");
                _session.Reset(clearFutureResponses: true);
            }

            PlaybackState currentState = _engine.State;

            if (currentState == PlaybackState.Idle)
            {
                if (CanLogDebug)
                    LogDebug($"Legacy begin stream: fps={chunk.FrameRate:F1}, channels={chunk.ChannelNames?.Count ?? 0}.");
                _engine.BeginStream(chunk.ChannelNames, chunk.FrameRate);

                if (ShouldOpenPlaybackGate(false))
                    OpenPlaybackGate();
                else
                    LogDebug("Legacy stream buffering: gate closed.");
            }

            _engine.FeedFrames(chunk.Frames);
            if (CanLogDebug)
                LogDebug(
                    $"Legacy feed: frames={chunk.FrameCount}, totalIngress={_engine.TotalIngressDuration:F3}s, buffered={_engine.BufferedDuration:F3}s, state='{_engine.State}'.");
        }

        private void FeedIndexedChunk(LipSyncPackedChunk chunk)
        {
            if (TryBufferFutureChunk(chunk)) return;

            if (CanLogDebug)
                LogDebug(
                    $"Indexed add: frames={chunk.FrameCount}, {ChunkSummary(chunk)}, expectedBefore={_session.Assembler.ExpectedFrameIndex}, pendingBefore={_session.Assembler.PendingFrameCount}.");
            bool hadOwnerBefore = _session.Assembler.HasActiveOwner;
            LipSyncTimelineOwner ownerBefore = _session.Assembler.ActiveOwner;
            int frameCountBefore = _session.Assembler.ExpectedFrameIndex;
            int recoveredBefore = _session.Assembler.RecoveredGapCount;
            LipSyncTimelineAssemblerResult result = _session.Assembler.AddChunk(chunk);
            if (CanLogDebug)
                LogDebug(
                    $"Indexed result: action='{result.Action}', emitFrames={result.FrameCount}, firstIndex={result.FirstFrameIndex}, owner={OwnerSummary(result.Owner)}, expectedAfter={_session.Assembler.ExpectedFrameIndex}, pendingAfter={_session.Assembler.PendingFrameCount}, engine='{_engine.State}'.");

            bool ownerAdopted = result.Action == LipSyncTimelineAssemblerAction.OwnerChanged ||
                                (!hadOwnerBefore && _session.Assembler.HasActiveOwner);
            if (ownerAdopted)
            {
                // Chunk arrival may lead audible audio by seconds. Reset ownership now, but bind
                // sample zero only at semantic/audio start below.
                _session.Timeline.OnOwnerAdopted(
                    _session.Assembler.ActiveOwner,
                    _isAudioPlaybackActiveForCharacter || _isCharacterSpeaking);
                _session.BeginActiveResponse();
                if (LipSyncDriftMonitor.Enabled)
                    LipSyncDriftMonitor.RecordEvent(_characterId, $"owner adopted: {result.Owner.ResponseId}");
            }

            switch (result.Action)
            {
                case LipSyncTimelineAssemblerAction.DropStaleOwner:
                    LogDebug("Indexed drop: stale owner or duplicate sequence.");
                    return;
                case LipSyncTimelineAssemblerAction.WaitingForGap:
                    LogDebug("Indexed waiting: timeline gap before next contiguous frame.");
                    return;
                case LipSyncTimelineAssemblerAction.None:
                    LogDebug("Indexed no-op.");
                    return;
                case LipSyncTimelineAssemblerAction.OwnerChanged:
                    LogResponseSummary(
                        IndexedLipSyncCompletionReason.Replaced,
                        ownerBefore,
                        frameCountBefore,
                        recoveredBefore);
                    if (_engine.State != PlaybackState.Idle)
                    {
                        if (CanLogDebug)
                            LogDebug($"Indexed owner changed: stopping old engine stream from state '{_engine.State}'.");
                        _engine.Stop();
                    }
                    break;
            }

            if (result.FrameCount <= 0)
            {
                LogDebug("Indexed result had no frames to feed.");
                return;
            }

            // A previous owner's tail can still be draining (truncate cancel or stats end); a new
            // owner's frames must not append to that timeline.
            if (_session.IsClosing && _engine.State != PlaybackState.Idle)
            {
                if (CanLogDebug)
                    LogDebug($"Indexed stream was ending: stopping drained stream from state '{_engine.State}'.");
                _engine.Stop();
            }

            if (_engine.State == PlaybackState.Idle || result.Action == LipSyncTimelineAssemblerAction.OwnerChanged)
            {
                if (CanLogDebug)
                    LogDebug($"Indexed begin stream: fps={result.FrameRate:F1}, channels={result.ChannelNames?.Count ?? 0}, sendAhead=true.");
                _engine.BeginStream(result.ChannelNames, result.FrameRate, sendAheadTimeline: true);
                _session.MarkStreamActive();

                if (ShouldOpenPlaybackGate(true))
                    ResumePlaybackGate();
                else
                    LogDebug("Indexed stream buffering: gate closed.");
            }

            // Frames are placed at their server timeline position (start_frame_index / fps), not at
            // arrival order, so sampling reflects the turn's real timeline.
            double timelineStart = result.FirstFrameIndex >= 0 && result.FrameRate > 0f
                ? result.FirstFrameIndex / (double)result.FrameRate
                : _engine.TotalIngressDuration;
            _engine.FeedFramesAt(result.Frames, timelineStart);
            if (CanLogDebug)
                LogDebug(
                    $"Indexed feed: frames={result.FrameCount}, timelineStart={timelineStart:F3}s, totalIngress={_engine.TotalIngressDuration:F3}s, buffered={_engine.BufferedDuration:F3}s, state='{_engine.State}'.");
        }

        private bool TryBufferFutureChunk(LipSyncPackedChunk chunk)
        {
            FutureChunkBufferResult result = _session.TryBufferFutureChunk(
                chunk,
                out string ownerKey,
                out string droppedOwnerKey);
            if (result == FutureChunkBufferResult.NotFuture) return false;

            if (droppedOwnerKey != null)
            {
                if (CanLogWarning)
                    LogWarning($"Future response queue full: dropped oldest buffered owner '{droppedOwnerKey}'.");
            }

            switch (result)
            {
                case FutureChunkBufferResult.Buffered:
                case FutureChunkBufferResult.DroppedCapacity:
                    if (CanLogDebug)
                        LogDebug($"Buffered future response '{ownerKey}'.");
                    break;
                case FutureChunkBufferResult.DroppedRetired:
                    LogDebug("Future chunk dropped: owner already retired.");
                    break;
                case FutureChunkBufferResult.DroppedDuration:
                    if (CanLogWarning)
                        LogWarning($"Future response '{ownerKey}' exceeded 3s buffer; dropped excess chunk.");
                    break;
                case FutureChunkBufferResult.DroppedInvalidOwner:
                    LogDebug("Future chunk dropped: incomplete canonical owner metadata.");
                    break;
            }

            return true;
        }

        private bool PromoteFutureResponse(LipSyncTimelineOwner owner)
        {
            if (!_session.TryTakeFutureResponse(
                    owner,
                    out string selectedKey,
                    out var chunks,
                    out int frameCount))
                return false;

            if (_session.Assembler.HasActiveOwner)
                _session.Assembler.CompleteActiveOwner();
            if (_engine.State != PlaybackState.Idle)
                _engine.Stop();

            _session.Reset(clearFutureResponses: false);
            if (CanLogDebug)
                LogDebug($"Promoting buffered response '{selectedKey}' with {frameCount} frames.");

            for (int i = 0; i < chunks.Count; i++) FeedIndexedChunk(chunks[i]);
            return true;
        }

        private void OnSpeechStateChanged(CharacterSpeechStateChanged evt)
        {
            if (_disposed || _eventHub == null) return;
            if (!IsForThisCharacter(evt.CharacterId)) return;
            if (evt.Timestamp == _lastLifecycleTimestamp && evt.IsSpeaking == _lastLifecycleSpeaking) return;

            HandleSpeechState(
                evt.IsSpeaking,
                new LipSyncTimelineOwner(evt.UtteranceId, null, null),
                evt.Timestamp);
        }

        private void OnResponseLifecycleChanged(LipSyncResponseLifecycleChanged evt)
        {
            if (_disposed || _eventHub == null) return;
            if (!IsForThisCharacter(evt.CharacterId)) return;

            _lastLifecycleTimestamp = evt.Timestamp;
            _lastLifecycleSpeaking = evt.IsSpeaking;
            HandleSpeechState(
                evt.IsSpeaking,
                new LipSyncTimelineOwner(evt.Owner.ResponseId, evt.Owner.TurnId, evt.Owner.Epoch),
                evt.Timestamp);
        }

        private void HandleSpeechState(bool isSpeaking, LipSyncTimelineOwner owner, DateTime timestamp)
        {
            bool authoritativeOwner = owner.HasOwner &&
                                      (owner.NeuroSyncTurnId.HasValue ||
                                       HasAuthoritativeSpeechOwner(owner.ResponseId));

            _isCharacterSpeaking = isSpeaking;
            if (isSpeaking && authoritativeOwner)
                PromoteFutureResponse(owner);
            else if (isSpeaking && !owner.HasOwner && !_session.Assembler.HasActiveOwner)
                PromoteFutureResponse(default(LipSyncTimelineOwner));
            if (CanLogDebug)
                LogDebug(
                    $"SpeechState: speaking={isSpeaking}, indexed={_session.HasIndexedStream}, audio={_isAudioPlaybackActiveForCharacter}, gate={ShouldOpenPlaybackGate(_session.HasIndexedStream)}, state='{_engine.State}', buffered={_engine.BufferedDuration:F3}s, {SyncTimingSummary(timestamp)}.");
            if (!isSpeaking)
            {
                if (_session.HasIndexedStream)
                {
                    if (authoritativeOwner &&
                        _session.Assembler.HasActiveOwner &&
                        !LipSyncTimelineAssembler.OwnersMatch(_session.Assembler.ActiveOwner, owner))
                    {
                        if (CanLogDebug)
                            LogDebug($"Speech stop ignored: {OwnerSummary(owner)} does not own active indexed stream.");
                        return;
                    }

                    _session.CompletionLastTarget = TryGetAudioTimelineTarget(out double target) ? target : -1d;
                    _session.CompletionWatchdogRemaining = CompletionStallTimeoutSeconds;
                    LogDebug("Speech stop armed sample-progress watchdog; waiting for stats, cancel, or true terminal stall.");
                    return;
                }

                LogDebug("Speech stop: notifying legacy stream end.");
                _engine.NotifyStreamEnd();
                return;
            }

            if (!_isAudioPlaybackActiveForCharacter)
            {
                if (owner.HasOwner) _session.Timeline.RecordLifecycleStart(owner, timestamp);
                if (CanLogDebug)
                    LogDebug(
                        $"Speech lifecycle arrived without audible audio; sample baseline deferred. {SyncTimingSummary(timestamp)}");
            }
            else if (!authoritativeOwner)
            {
                _session.Timeline.RecordAudioStart(timestamp);
            }
            else if (!_session.Assembler.HasActiveOwner)
            {
                _session.Timeline.RecordOwnedAudioStart(owner, timestamp);
            }
            else if (LipSyncTimelineAssembler.OwnersMatch(_session.Assembler.ActiveOwner, owner))
            {
                _session.Timeline.RecordAudioStart(timestamp);
            }
            else
            {
                _session.Timeline.RecordOwnedAudioStart(owner, timestamp);
                if (CanLogDebug)
                    LogDebug($"Speech start {OwnerSummary(owner)} does not match active indexed owner; baseline buffered.");
            }

            if (!ShouldOpenPlaybackGate(_session.HasIndexedStream))
            {
                if (CanLogDebug)
                    LogDebug(
                        $"Speech start did not open gate: no current audible audio-start signal. {SyncTimingSummary(timestamp)}");
                return;
            }

            if (_engine.State == PlaybackState.Idle)
            {
                LogDebug("Speech start noted while engine idle: gate deferred until a stream begins.");
                return;
            }

            OpenPlaybackGate();
        }

        private void OnBlendshapeTurnStatsReceived(BlendshapeTurnStatsReceived evt)
        {
            if (_disposed || _eventHub == null) return;
            if (!_session.HasIndexedStream) return;
            if (!IsForThisCharacter(evt.CharacterId)) return;

            // Stats from a different (older/newer) response must not end the active stream.
            bool ownerMatched = !evt.HasOwnerMetadata ||
                                !_session.Assembler.HasActiveOwner ||
                                _session.Assembler.IsActiveOwner(evt.ResponseId, evt.NeuroSyncTurnId, evt.Epoch);
            if (CanLogDebug)
                LogDebug(
                    $"TurnStats: server={evt.TotalBlendshapes}, client={evt.ReceivedBlendshapeFrames}, matches={evt.FrameCountMatches}, ownerMatched={ownerMatched}, response='{evt.ResponseId}', turn={evt.NeuroSyncTurnId?.ToString() ?? "null"}, epoch={evt.Epoch?.ToString() ?? "null"}, turnMs={evt.TotalTurnDurationMs:F1}, audioMs={evt.TotalAudioDurationMs:F1}, fps={evt.Fps:F1}, state='{_engine.State}'.");
            if (!ownerMatched)
            {
                LogDebug("TurnStats ignored: stats owner does not match active stream owner.");
                return;
            }

            if (!evt.FrameCountMatches)
            {
                // The missing frames may still be in flight; give them a short grace window, then
                // end anyway so a lost trailing chunk cannot leave the stream open forever.
                _session.StatsGraceRemaining = StatsMismatchGraceSeconds;
                if (CanLogDebug)
                    LogDebug($"TurnStats frame counts do not match: armed {StatsMismatchGraceSeconds:F2}s grace window.");
                if (LipSyncDriftMonitor.Enabled)
                    LipSyncDriftMonitor.RecordEvent(_characterId,
                        $"stats mismatch: {evt.ReceivedBlendshapeFrames}/{evt.TotalBlendshapes}, grace armed");
                return;
            }

            // Even matching stats can precede a final transport tick. Always retain the same
            // deterministic late-chunk window; otherwise a long send-ahead response can fade at
            // its provisional end before its final chunk reaches Unity.
            _session.StatsGraceRemaining = StatsMismatchGraceSeconds;
            if (CanLogDebug)
                LogDebug($"TurnStats matched: armed {StatsMismatchGraceSeconds:F2}s late-chunk grace window.");
            if (LipSyncDriftMonitor.Enabled)
                LipSyncDriftMonitor.RecordEvent(_characterId, "stats matched: late-chunk grace armed");
        }

        private void OnTimelineResetRequested(LipSyncTimelineResetRequested evt)
        {
            if (_disposed || _eventHub == null) return;
            if (!IsForThisCharacter(evt.CharacterId)) return;

            if (CanLogDebug)
                LogDebug(
                    $"TimelineReset: reason='{evt.Reason}', response='{evt.ResponseId}', turn={evt.NeuroSyncTurnId?.ToString() ?? "null"}, epoch={evt.Epoch?.ToString() ?? "null"}, sequence={evt.Sequence?.ToString() ?? "null"}, validThrough={evt.ValidThroughFrameIndex?.ToString() ?? "null"}, state='{_engine.State}'.");
            LipSyncTimelineOwner ownerBefore = _session.Assembler.ActiveOwner;
            int frameCountBefore = _session.Assembler.ExpectedFrameIndex;
            int recoveredBefore = _session.Assembler.RecoveredGapCount;
            LipSyncTimelineAssemblerResult result = _session.Assembler.CancelOwner(evt);
            if (CanLogDebug)
                LogDebug($"TimelineReset result: action='{result.Action}', activeOwner={OwnerSummary(result.Owner)}.");

            if (result.Action == LipSyncTimelineAssemblerAction.TruncateAfter &&
                result.ValidThroughFrameIndex.HasValue)
            {
                // Audio through valid_through_frame_index was already released and keeps playing;
                // only the unreleased future is discarded. The engine drains the kept tail and fades.
                float frameRate = Math.Max(1f, result.FrameRate);
                double cutoffSeconds = (result.ValidThroughFrameIndex.Value + 1) / (double)frameRate;
                _engine.TruncateAfter(cutoffSeconds);
                _session.BeginClosing(IndexedLipSyncCompletionReason.CancelledTruncate);
                _session.StatsGraceRemaining = -1f;
                _session.CompletionWatchdogRemaining = -1f;
                _session.CompletionLastTarget = -1d;
                _session.ClosedInputBoundarySeconds = null;
                _session.Timeline.ClearActiveAnchorMatch();
                LogResponseSummary(
                    IndexedLipSyncCompletionReason.CancelledTruncate,
                    ownerBefore,
                    frameCountBefore,
                    recoveredBefore);
                _session.MarkTerminalSummaryLogged();
                if (CanLogDebug)
                    LogDebug(
                        $"TimelineReset truncate: keeping frames through index {result.ValidThroughFrameIndex.Value} ({cutoffSeconds:F3}s), stream marked ending.");
                if (LipSyncDriftMonitor.Enabled)
                    LipSyncDriftMonitor.RecordEvent(_characterId,
                        $"cancel: truncate @{cutoffSeconds:F2}s ({evt.Reason})");
                return;
            }

            if (result.Action != LipSyncTimelineAssemblerAction.HardReset) return;

            LogResponseSummary(
                IndexedLipSyncCompletionReason.CancelledReset,
                ownerBefore,
                frameCountBefore,
                recoveredBefore);
            _engine.StopSmooth();
            _session.Reset(clearFutureResponses: false);
            LogDebug("TimelineReset hard reset: engine fading out and indexed state cleared.");
            if (LipSyncDriftMonitor.Enabled)
                LipSyncDriftMonitor.RecordEvent(_characterId, $"cancel: hard reset ({evt.Reason})");
            PromoteFutureResponse(default);
        }

        private bool IsForThisCharacter(string eventCharacterId)
        {
            if (string.IsNullOrWhiteSpace(_characterId)) return false;

            if (string.IsNullOrWhiteSpace(eventCharacterId)) return false;

            return string.Equals(_characterId, eventCharacterId, StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasAuthoritativeSpeechOwner(string utteranceId) =>
            !string.IsNullOrWhiteSpace(utteranceId) &&
            utteranceId.IndexOf(":character-response-", StringComparison.Ordinal) < 0;

        /// <summary>
        ///     Whether playback may start. A new indexed response requires a current audible-audio
        ///     signal. Once opened, its sample clock may keep the gate alive through natural silence
        ///     or underrun, but silent PCM must never bootstrap a response after audible audio ended.
        /// </summary>
        private bool ShouldOpenPlaybackGate(bool indexed)
        {
            if (indexed && _session.Timeline.IsBrowserMediaClockArmed &&
                !_session.Timeline.IsBrowserMediaClockBound)
                return false;
            if (_isAudioPlaybackActiveForCharacter) return true;
            if (!indexed || !_session.GateOpen || !_session.Timeline.IsAuthoritativeClockBound) return false;

            if (_session.Timeline.IsSampleClockBound)
            {
                AudioTimelineSnapshot? sampleSnapshot = TryReadAudioTimelineSnapshot();
                return sampleSnapshot.HasValue &&
                       (sampleSnapshot.Value.State == AudioTimelinePlaybackState.Playing ||
                        sampleSnapshot.Value.State == AudioTimelinePlaybackState.Underrun);
            }

            AudioMediaTimelineSnapshot? mediaSnapshot = TryReadAudioMediaTimelineSnapshot();
            return mediaSnapshot.HasValue &&
                   (mediaSnapshot.Value.State == AudioTimelinePlaybackState.Playing ||
                    mediaSnapshot.Value.State == AudioTimelinePlaybackState.Underrun);
        }

        private void OpenPlaybackGate()
        {
            if (CanLogDebug)
                LogDebug(
                    $"Gate open: state='{_engine.State}', indexed={_session.HasIndexedStream}, audio={_isAudioPlaybackActiveForCharacter}, speaking={_isCharacterSpeaking}, {SyncTimingSummary(DateTime.UtcNow)}.");
            if (LipSyncDriftMonitor.Enabled) LipSyncDriftMonitor.RecordEvent(_characterId, "gate open");
            _playbackGateOpened?.Invoke();
            _session.OpenGate();
            _engine.NotifyAudioPlaybackStarted();
        }

        private void ResumePlaybackGate()
        {
            if (CanLogDebug)
                LogDebug(
                    $"Gate resume: state='{_engine.State}', indexed={_session.HasIndexedStream}, audio={_isAudioPlaybackActiveForCharacter}, speaking={_isCharacterSpeaking}, {SyncTimingSummary(DateTime.UtcNow)}.");
            if (LipSyncDriftMonitor.Enabled) LipSyncDriftMonitor.RecordEvent(_characterId, "gate resume");
            if (_playbackGateResumed != null)
                _playbackGateResumed.Invoke();
            else
                _playbackGateOpened?.Invoke();

            _session.OpenGate();
            _engine.NotifyAudioPlaybackStarted();
        }

        private bool CanLogInfo =>
            _logger?.IsEnabled(LogLevel.Info, LogCategory.LipSync) ??
            LoggingConfig.IsInfoEnabled(LogCategory.LipSync);

        private bool CanLogWarning =>
            _logger?.IsEnabled(LogLevel.Warning, LogCategory.LipSync) ??
            LoggingConfig.IsWarningEnabled(LogCategory.LipSync);

        private bool CanLogDebug =>
            _logger?.IsEnabled(LogLevel.Debug, LogCategory.LipSync) ??
            LoggingConfig.IsDebugEnabled(LogCategory.LipSync);

        private void LogInfo(string message)
        {
            if (!CanLogInfo) return;

            if (_logger != null)
            {
                _logger.Info($"{LogPrefix} {message}", LogCategory.LipSync);
                return;
            }

            ConvaiLogger.Info(message, LogCategory.LipSync);
        }

        private void LogWarning(string message)
        {
            if (!CanLogWarning) return;

            if (_logger != null)
            {
                _logger.Warning($"{LogPrefix} {message}", LogCategory.LipSync);
                return;
            }

            ConvaiLogger.Warning(message, LogCategory.LipSync);
        }

        /// <summary>
        ///     Per-message-frequency diagnostics (per chunk / per event). [Conditional] removes the
        ///     call AND its argument evaluation from release builds, so the interpolated strings on
        ///     the hot path cost nothing in production.
        /// </summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        [System.Diagnostics.Conditional("CONVAI_DEBUG_LOGGING")]
        private void LogDebug(string message)
        {
            if (!CanLogDebug) return;

            if (_logger != null)
            {
                _logger.Debug($"{LogPrefix} {message}", LogCategory.LipSync);
                return;
            }

            ConvaiLogger.Debug(message, LogCategory.LipSync);
        }

        private static string ChunkSummary(LipSyncPackedChunk chunk)
        {
            if (chunk == null) return "chunk=null";

            return
                $"profile='{chunk.ProfileId}', fps={chunk.FrameRate:F1}, owner=response:{FormatValue(chunk.ResponseId)},turn:{FormatValue(chunk.NeuroSyncTurnId)},epoch:{FormatValue(chunk.Epoch)}, timeline=start:{FormatValue(chunk.StartFrameIndex)},seq:{FormatValue(chunk.Sequence)}";
        }

        private static string OwnerSummary(LipSyncTimelineOwner owner) =>
            $"response:{FormatValue(owner.ResponseId)},turn:{FormatValue(owner.NeuroSyncTurnId)},epoch:{FormatValue(owner.Epoch)}";

        private void LogResponseSummary(IndexedLipSyncCompletionReason reason)
        {
            if (_session.TerminalSummaryLogged) return;

            LogResponseSummary(
                reason,
                _session.Assembler.ActiveOwner,
                _session.Assembler.ExpectedFrameIndex,
                _session.Assembler.RecoveredGapCount);
            _session.MarkTerminalSummaryLogged();
        }

        private void LogResponseSummary(
            IndexedLipSyncCompletionReason reason,
            LipSyncTimelineOwner owner,
            int frameCount,
            int recoveredGapCount)
        {
            if (!CanLogInfo) return;

            LogInfo(
                $"Response complete: owner={OwnerSummary(owner)}, frames={frameCount}, recoveredGaps={recoveredGapCount}, clock={_session.Timeline.ClockMode}, reason={reason}.");
        }

        private string SyncTimingSummary(DateTime eventTimestamp)
        {
            string sampleSummary = "sample=unavailable";
            AudioTimelineSnapshot? snapshot = TryReadAudioTimelineSnapshot();
            if (snapshot.HasValue)
            {
                AudioTimelineSnapshot value = snapshot.Value;
                sampleSummary =
                    $"sample=state:{value.State},frame:{value.AbsoluteSourceFrame},rate:{value.SampleRate},buffered:{value.BufferedFrames},rendered:{value.RenderedFrames},skipped:{value.SkippedFrames},underruns:{value.UnderrunCount}";
            }

            string mediaSummary = "media=unavailable";
            AudioMediaTimelineSnapshot? mediaSnapshot = TryReadAudioMediaTimelineSnapshot();
            if (mediaSnapshot.HasValue)
            {
                AudioMediaTimelineSnapshot value = mediaSnapshot.Value;
                mediaSummary =
                    $"media=state:{value.State},position:{value.LogicalPositionSeconds:F3},signal:{value.SignalGeneration}@{value.SignalStartPositionSeconds:F3},analyser:{value.AnalyserAvailable},stalls:{value.StallCount},replacements:{value.ElementReplacementCount},discontinuity:{value.DiscontinuityGeneration}";
            }

            return
                $"sync=audioActive:{_isAudioPlaybackActiveForCharacter},gateOpen:{_session.GateOpen},clock:{_session.Timeline.ClockMode},browserSignalTimeout:{_session.Timeline.BrowserSignalTimedOut},browserFallbacks:{_session.Timeline.BrowserFallbackCount},afterAudioStartMs:{ElapsedMilliseconds(_lastAudioPlaybackStartedUtc, eventTimestamp)},afterAudioStopMs:{ElapsedMilliseconds(_lastAudioPlaybackStoppedUtc, eventTimestamp)},{sampleSummary},{mediaSummary}";
        }

        private static string ElapsedMilliseconds(DateTime? originUtc, DateTime timestampUtc) =>
            originUtc.HasValue ? (timestampUtc - originUtc.Value).TotalMilliseconds.ToString("F1") : "none";

        private static string FormatValue(string value) => string.IsNullOrWhiteSpace(value) ? "null" : value;

        private static string FormatValue(int? value) => value.HasValue ? value.Value.ToString() : "null";
    }
}
