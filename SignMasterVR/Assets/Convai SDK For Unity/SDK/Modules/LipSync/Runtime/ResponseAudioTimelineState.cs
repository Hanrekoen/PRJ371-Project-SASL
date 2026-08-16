using System;
using System.Collections.Generic;
using Convai.Infrastructure.Networking;

namespace Convai.Modules.LipSync
{
    /// <summary>Outcome of feeding a server audio timeline anchor into the state.</summary>
    internal enum AnchorRegistration
    {
        /// <summary>Anchor matched the active owner for the first time; the playback gate may reopen.</summary>
        MatchedActiveOwner,

        /// <summary>The active owner already had a matching anchor; nothing changed.</summary>
        AlreadyMatched,

        /// <summary>Anchor belongs to a future owner and was buffered for adoption.</summary>
        BufferedForFutureOwner,

        /// <summary>An anchor for that owner was already buffered; dropped.</summary>
        Duplicate,

        /// <summary>Anchor sequence moved backwards or repeated for the same owner.</summary>
        Stale
    }

    internal enum SampleAnchorRegistration
    {
        Accepted,
        IgnoredWithoutFinalSample,
        InvalidBounds
    }

    /// <summary>
    ///     Response-owned audio timeline state for the send-ahead lip sync path.
    ///     The turn's visual timeline is start_frame_index/fps; the audio position within the turn is
    ///     timelineBase + (raw playhead - rawBase), where the raw playhead counts source audio actually
    ///     rendered since the current playback signal began. Also tracks server audio timeline anchors
    ///     (neurosync-audio-timeline-anchor): only the first matching anchor per owner may set the
    ///     timeline base. Anchors remain supplemental and never gate current-backend playback.
    ///     Pure C# — the playhead and clock are injected for testability.
    /// </summary>
    internal sealed class ResponseAudioTimelineState
    {
        private const int MaxPendingAnchors = 4;
        private static readonly TimeSpan BrowserSignalWait = TimeSpan.FromMilliseconds(500);
        private const double RecentBrowserSignalSeconds = 0.5d;

        private readonly Func<double?> _rawPlayheadProvider;
        private readonly Func<AudioTimelineSnapshot?> _sampleSnapshotProvider;
        private readonly Func<AudioMediaTimelineSnapshot?> _mediaSnapshotProvider;
        private readonly Func<DateTime> _utcNow;
        private readonly List<(LipSyncTimelineOwner Owner, double BaseSeconds)> _pendingAnchors = new();
        private readonly List<(LipSyncTimelineOwner Owner, double FinalSeconds)> _pendingFinalSamples = new();
        private readonly Dictionary<string, int> _lastAnchorSequenceByOwner = new(StringComparer.Ordinal);

        private DateTime? _audioStartUtc;
        private double _timelineBaseSeconds;
        private double _rawPlayheadBaseSeconds;
        private bool _sampleClockBound;
        private long _sampleBaselineFrame;
        private long _lastSampleFrame;
        private int _sampleRate;
        private int _sampleFormatGeneration;
        private int _sampleSignalGeneration;
        private int _lastDiscontinuityGeneration;
        private bool _sampleDiscontinuityPending;
        private bool _mediaClockArmed;
        private bool _mediaClockBound;
        private bool _mediaClockFallback;
        private double _mediaBaselineSeconds;
        private double _mediaLifecycleBaselineSeconds;
        private double _lastMediaPositionSeconds;
        private int _mediaArmedSignalGeneration;
        private int _mediaSignalGeneration;
        private int _lastMediaDiscontinuityGeneration;
        private DateTime _mediaSignalDeadlineUtc;
        private bool _mediaDiscontinuityPending;
        private bool _mediaAnalyserAvailable;
        private int _mediaStallCount;
        private int _mediaElementReplacementCount;
        private double? _finalTimelineSeconds;
        private LipSyncTimelineOwner _pendingAudioOwner;
        private bool _hasPendingSampleBaseline;
        private long _pendingSampleBaselineFrame;
        private int _pendingSampleRate;
        private int _pendingSampleFormatGeneration;
        private int _pendingSignalGeneration;
        private int _pendingDiscontinuityGeneration;
        private bool _hasPendingMediaCapability;
        private bool _hasPendingExactMediaBaseline;
        private double _pendingMediaBaselineSeconds;
        private double _pendingMediaLifecycleBaselineSeconds;
        private int _pendingMediaSignalGeneration;
        private int _pendingMediaDiscontinuityGeneration;
        private bool _pendingMediaAnalyserAvailable;
        private DateTime? _pendingAudioStartUtc;

        /// <param name="rawPlayheadProvider">
        ///     Returns seconds of source audio rendered since the current playback signal started, or
        ///     null when the platform exposes no measured playhead (the state then falls back to wall
        ///     clock since the audio-start event).
        /// </param>
        /// <param name="utcNow">Clock override for tests; defaults to <see cref="DateTime.UtcNow" />.</param>
        public ResponseAudioTimelineState(Func<double?> rawPlayheadProvider, Func<DateTime> utcNow = null)
            : this(null, null, rawPlayheadProvider, utcNow)
        {
        }

        public ResponseAudioTimelineState(
            Func<AudioTimelineSnapshot?> sampleSnapshotProvider,
            Func<double?> rawPlayheadProvider,
            Func<DateTime> utcNow = null)
            : this(sampleSnapshotProvider, null, rawPlayheadProvider, utcNow)
        {
        }

        public ResponseAudioTimelineState(
            Func<AudioTimelineSnapshot?> sampleSnapshotProvider,
            Func<AudioMediaTimelineSnapshot?> mediaSnapshotProvider,
            Func<double?> rawPlayheadProvider,
            Func<DateTime> utcNow = null)
        {
            _sampleSnapshotProvider = sampleSnapshotProvider;
            _mediaSnapshotProvider = mediaSnapshotProvider;
            _rawPlayheadProvider = rawPlayheadProvider;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        /// <summary>True when the active owner has (or is treated as having) a matching anchor.</summary>
        public bool ActiveOwnerAnchorMatched { get; private set; }

        /// <summary>Turn timeline position (seconds) where the current playback signal started.</summary>
        public double TimelineBaseSeconds => _timelineBaseSeconds;
        public bool IsSampleClockBound => _sampleClockBound;
        public bool IsBrowserMediaClockBound => _mediaClockBound;
        public bool IsAuthoritativeClockBound => _sampleClockBound || _mediaClockBound;
        public bool IsBrowserMediaClockArmed => _mediaClockArmed;
        public bool BrowserAnalyserAvailable => _mediaAnalyserAvailable;
        public bool BrowserSignalTimedOut { get; private set; }
        public int BrowserFallbackCount { get; private set; }
        public int BrowserStallCount => _mediaStallCount;
        public int BrowserElementReplacementCount => _mediaElementReplacementCount;
        public AudioTimelineClockMode ClockMode => _sampleClockBound
            ? AudioTimelineClockMode.SampleLocked
            : _mediaClockBound
                ? _mediaClockFallback
                    ? AudioTimelineClockMode.BrowserMediaFallback
                    : AudioTimelineClockMode.BrowserMediaLocked
            : _rawPlayheadProvider?.Invoke().HasValue == true
                ? AudioTimelineClockMode.LegacyPlayhead
                : AudioTimelineClockMode.WallClock;

        public void Reset()
        {
            _audioStartUtc = null;
            _timelineBaseSeconds = 0d;
            _rawPlayheadBaseSeconds = 0d;
            _pendingAnchors.Clear();
            _pendingFinalSamples.Clear();
            _lastAnchorSequenceByOwner.Clear();
            ActiveOwnerAnchorMatched = false;
            _sampleClockBound = false;
            _sampleBaselineFrame = 0;
            _lastSampleFrame = 0;
            _sampleRate = 0;
            _sampleFormatGeneration = 0;
            _sampleSignalGeneration = 0;
            _lastDiscontinuityGeneration = 0;
            _sampleDiscontinuityPending = false;
            ResetMediaClock();
            BrowserFallbackCount = 0;
            _finalTimelineSeconds = null;
            ClearPendingAudioStart();
        }

        /// <summary>A new playback signal began: the raw playhead restarts from zero at this instant.</summary>
        public void RecordAudioStart(DateTime timestampUtc)
        {
            CapturePendingAudioBaseline(timestampUtc);
            // Repeated browser "playing" events after a stall or natural speech pause belong to
            // the same response. Once armed/bound, they must not select a later signal generation
            // as a new response zero.
            if (_sampleClockBound || _mediaClockArmed || _mediaClockBound) return;
            if (TryBindSampleClock()) return;
            if (TryArmOrBindMediaClock(timestampUtc)) return;

            _audioStartUtc = timestampUtc;
            _rawPlayheadBaseSeconds = 0d;
        }

        /// <summary>
        ///     Records response ownership without treating a data-channel lifecycle event as proof
        ///     that audible audio has started. The next real audio-start event captures the baseline.
        /// </summary>
        public void RecordLifecycleStart(LipSyncTimelineOwner owner, DateTime timestampUtc)
        {
            if (!owner.HasOwner) return;

            _pendingAudioOwner = owner;
            _pendingAudioStartUtc = timestampUtc;
            _hasPendingSampleBaseline = false;
            CapturePendingMediaBaseline(timestampUtc);
        }

        /// <summary>
        ///     Captures an owner-scoped source baseline even when response audio starts before its
        ///     first animation chunk arrives. Owner adoption consumes this baseline exactly once.
        /// </summary>
        public void RecordOwnedAudioStart(LipSyncTimelineOwner owner, DateTime timestampUtc)
        {
            if (!owner.HasOwner)
            {
                RecordAudioStart(timestampUtc);
                return;
            }

            RecordLifecycleStart(owner, timestampUtc);
            CapturePendingAudioBaseline(timestampUtc);
        }

        /// <summary>
        ///     Playback signal ended: freeze the turn position where audio actually halted so a
        ///     mid-turn gap resumes from there instead of drifting through the silence.
        /// </summary>
        public void RecordAudioStop()
        {
            // Amplitude silence is not a sample-clock stop. Zero PCM remains part of the response,
            // while a true underrun naturally freezes AbsoluteSourceFrame.
            if (_sampleClockBound || _mediaClockArmed || _mediaClockBound) return;

            if (!_audioStartUtc.HasValue) return;

            _timelineBaseSeconds += CurrentRawPlayheadSeconds();
            _rawPlayheadBaseSeconds = 0d;
            _audioStartUtc = null;
        }

        /// <summary>
        ///     Re-bases the turn timeline when a new response owner is adopted. If audio from the
        ///     previous turn is still audible (no signal gap between turns), the raw playhead keeps
        ///     counting, so the current reading becomes the new turn's zero. Adopts a buffered anchor
        ///     for the owner when one is pending.
        /// </summary>
        public void OnOwnerAdopted(LipSyncTimelineOwner activeOwner, bool audioActive)
        {
            bool ownsPendingAudio = _pendingAudioOwner.HasOwner &&
                                    LipSyncTimelineAssembler.OwnersMatch(_pendingAudioOwner, activeOwner);
            bool usePendingSamples = ownsPendingAudio && _hasPendingSampleBaseline;
            bool usePendingMedia = ownsPendingAudio && !usePendingSamples && _hasPendingMediaCapability;
            DateTime? pendingStartUtc = ownsPendingAudio ? _pendingAudioStartUtc : null;

            _timelineBaseSeconds = 0d;
            _rawPlayheadBaseSeconds = 0d;
            _audioStartUtc = null;
            _sampleClockBound = false;
            _sampleBaselineFrame = 0;
            _lastSampleFrame = 0;
            _sampleRate = 0;
            _sampleFormatGeneration = 0;
            _sampleSignalGeneration = 0;
            _lastDiscontinuityGeneration = 0;
            _sampleDiscontinuityPending = false;
            ResetActiveMediaClock();
            _finalTimelineSeconds = null;

            ActiveOwnerAnchorMatched = false;

            for (int i = 0; i < _pendingAnchors.Count; i++)
            {
                if (!OwnersMatch(_pendingAnchors[i].Owner, activeOwner)) continue;

                ActiveOwnerAnchorMatched = true;
                _timelineBaseSeconds = _pendingAnchors[i].BaseSeconds;
                _pendingAnchors.RemoveAt(i);
                break;
            }

            for (int i = 0; i < _pendingFinalSamples.Count; i++)
            {
                if (!OwnersMatch(_pendingFinalSamples[i].Owner, activeOwner)) continue;

                _finalTimelineSeconds = _pendingFinalSamples[i].FinalSeconds;
                _pendingFinalSamples.RemoveAt(i);
                break;
            }

            if (usePendingSamples)
            {
                _sampleClockBound = true;
                _sampleBaselineFrame = _pendingSampleBaselineFrame;
                _lastSampleFrame = _pendingSampleBaselineFrame;
                _sampleRate = _pendingSampleRate;
                _sampleFormatGeneration = _pendingSampleFormatGeneration;
                _sampleSignalGeneration = _pendingSignalGeneration;
                _lastDiscontinuityGeneration = _pendingDiscontinuityGeneration;
            }
            else if (usePendingMedia)
            {
                if (_hasPendingExactMediaBaseline)
                {
                    BindMediaClock(
                        _pendingMediaBaselineSeconds,
                        _pendingMediaSignalGeneration,
                        _pendingMediaDiscontinuityGeneration,
                        fallback: false);
                }
                else
                {
                    ArmMediaClock(
                        _pendingMediaLifecycleBaselineSeconds,
                        _pendingMediaSignalGeneration,
                        pendingStartUtc.GetValueOrDefault(_utcNow()),
                        _pendingMediaDiscontinuityGeneration,
                        _pendingMediaAnalyserAvailable);
                }
            }
            else if (pendingStartUtc.HasValue)
            {
                if (!TryArmOrBindMediaClock(pendingStartUtc.Value))
                    _audioStartUtc = pendingStartUtc.Value;
            }
            else if (audioActive)
            {
                RecordAudioStart(_utcNow());
            }

            if (ownsPendingAudio) ClearPendingAudioStart();
        }

        public SampleAnchorRegistration RegisterSampleAnchor(
            LipSyncTimelineOwner owner,
            int sampleRate,
            long responseAudioStartSample,
            long? finalAudioSample,
            bool matchesActiveOwner)
        {
            if (sampleRate <= 0 || responseAudioStartSample < 0 ||
                finalAudioSample.HasValue && finalAudioSample.Value < responseAudioStartSample)
                return SampleAnchorRegistration.InvalidBounds;
            if (!finalAudioSample.HasValue) return SampleAnchorRegistration.IgnoredWithoutFinalSample;

            double finalSeconds = Math.Max(responseAudioStartSample, finalAudioSample.Value) /
                                  (double)sampleRate;
            if (matchesActiveOwner)
            {
                _finalTimelineSeconds = finalSeconds;
                return SampleAnchorRegistration.Accepted;
            }

            for (int i = 0; i < _pendingFinalSamples.Count; i++)
            {
                if (!OwnersMatch(_pendingFinalSamples[i].Owner, owner)) continue;
                _pendingFinalSamples[i] = (owner, finalSeconds);
                return SampleAnchorRegistration.Accepted;
            }

            _pendingFinalSamples.Add((owner, finalSeconds));
            while (_pendingFinalSamples.Count > MaxPendingAnchors) _pendingFinalSamples.RemoveAt(0);
            return SampleAnchorRegistration.Accepted;
        }

        public bool TryGetFinalTimelineSeconds(out double finalSeconds)
        {
            finalSeconds = _finalTimelineSeconds.GetValueOrDefault();
            return _finalTimelineSeconds.HasValue;
        }

        /// <summary>
        ///     Current position on the active turn's audio timeline in seconds, measured from the
        ///     audio actually rendered to the device. Valid only while a playback signal is active.
        /// </summary>
        public bool TryGetTarget(out double targetSeconds)
        {
            targetSeconds = 0d;
            if (_sampleClockBound && TryGetSampleSnapshot(out AudioTimelineSnapshot snapshot))
            {
                if (snapshot.FormatGeneration != _sampleFormatGeneration || snapshot.SampleRate != _sampleRate)
                {
                    if (_sampleRate > 0)
                        _timelineBaseSeconds += Math.Max(0d,
                            (_lastSampleFrame - _sampleBaselineFrame) / (double)_sampleRate);

                    _sampleBaselineFrame = snapshot.AbsoluteSourceFrame;
                    _sampleFormatGeneration = snapshot.FormatGeneration;
                    _sampleRate = snapshot.SampleRate;
                    _sampleSignalGeneration = snapshot.SignalGeneration;
                    _lastDiscontinuityGeneration = snapshot.DiscontinuityGeneration;
                }

                if (snapshot.DiscontinuityGeneration != _lastDiscontinuityGeneration)
                {
                    _sampleDiscontinuityPending = true;
                    _lastDiscontinuityGeneration = snapshot.DiscontinuityGeneration;
                }

                _lastSampleFrame = snapshot.AbsoluteSourceFrame;
                targetSeconds = _timelineBaseSeconds + Math.Max(0d,
                    (snapshot.AbsoluteSourceFrame - _sampleBaselineFrame) / (double)_sampleRate);
                return snapshot.State != AudioTimelinePlaybackState.Disposed;
            }

            if ((_mediaClockArmed || _mediaClockBound) && TryGetMediaTarget(out targetSeconds))
                return true;

            if (!_audioStartUtc.HasValue) return false;

            targetSeconds = _timelineBaseSeconds + CurrentRawPlayheadSeconds();
            return true;
        }

        /// <summary>
        /// Returns true once when the native audio source jumps over discarded samples. The visual
        /// engine uses this edge to blend into the new pose instead of exposing an abrupt mouth snap.
        /// </summary>
        public bool ConsumeSampleDiscontinuity()
        {
            if (!_sampleDiscontinuityPending) return false;

            _sampleDiscontinuityPending = false;
            return true;
        }

        /// <summary>Returns true once when either authoritative playback clock reports a jump.</summary>
        public bool ConsumeAuthoritativeDiscontinuity()
        {
            if (ConsumeSampleDiscontinuity()) return true;
            if (!_mediaDiscontinuityPending) return false;

            _mediaDiscontinuityPending = false;
            return true;
        }

        /// <summary>
        ///     Registers a server audio timeline anchor. The caller decides owner identity
        ///     (<paramref name="matchesActiveOwner" /> via the timeline assembler's alias matching).
        ///     Only the first anchor per owner is kept; later anchors describe release positions
        ///     (ahead of playback) and must not drag the playhead forward, so the timeline base is
        ///     adopted only while audio has not started.
        /// </summary>
        public AnchorRegistration RegisterAnchor(
            LipSyncTimelineOwner anchorOwner,
            double baseSeconds,
            bool matchesActiveOwner,
            int? sequence)
        {
            string ownerKey = LipSyncTimelineAssembler.CanonicalOwnerKey(anchorOwner);
            if (sequence.HasValue && ownerKey != null)
            {
                if (_lastAnchorSequenceByOwner.TryGetValue(ownerKey, out int previous) && sequence.Value <= previous)
                    return AnchorRegistration.Stale;
                _lastAnchorSequenceByOwner[ownerKey] = sequence.Value;
            }

            if (matchesActiveOwner)
            {
                if (ActiveOwnerAnchorMatched) return AnchorRegistration.AlreadyMatched;

                ActiveOwnerAnchorMatched = true;
                _timelineBaseSeconds = baseSeconds;
                return AnchorRegistration.MatchedActiveOwner;
            }

            for (int i = 0; i < _pendingAnchors.Count; i++)
            {
                if (OwnersMatch(_pendingAnchors[i].Owner, anchorOwner)) return AnchorRegistration.Duplicate;
            }

            _pendingAnchors.Add((anchorOwner, baseSeconds));
            while (_pendingAnchors.Count > MaxPendingAnchors) _pendingAnchors.RemoveAt(0);
            return AnchorRegistration.BufferedForFutureOwner;
        }

        /// <summary>The active owner's stream ended (truncate/hard reset); its anchor match is void.</summary>
        public void ClearActiveAnchorMatch()
        {
            ActiveOwnerAnchorMatched = false;
            _finalTimelineSeconds = null;
        }

        /// <summary>
        ///     Seconds of the active turn's audio played since the current playback signal started.
        ///     Uses the injected device-measured playhead when available (freezes on underrun,
        ///     accounts for drift skips); otherwise falls back to wall clock since the audio-start event.
        /// </summary>
        private double CurrentRawPlayheadSeconds()
        {
            double? played = _rawPlayheadProvider?.Invoke();
            if (played.HasValue)
                return Math.Max(0d, played.Value - _rawPlayheadBaseSeconds);

            if (!_audioStartUtc.HasValue) return 0d;

            return Math.Max(0d, (_utcNow() - _audioStartUtc.Value).TotalSeconds - _rawPlayheadBaseSeconds);
        }

        private bool TryBindSampleClock()
        {
            if (!TryGetSampleSnapshot(out AudioTimelineSnapshot snapshot)) return false;

            _sampleClockBound = true;
            _sampleBaselineFrame = SelectSampleBaseline(snapshot);
            _lastSampleFrame = snapshot.AbsoluteSourceFrame;
            _sampleRate = snapshot.SampleRate;
            _sampleFormatGeneration = snapshot.FormatGeneration;
            _sampleSignalGeneration = snapshot.SignalGeneration;
            _lastDiscontinuityGeneration = snapshot.DiscontinuityGeneration;
            _sampleDiscontinuityPending = false;
            _audioStartUtc = null;
            _rawPlayheadBaseSeconds = 0d;
            return true;
        }

        private bool TryArmOrBindMediaClock(DateTime timestampUtc)
        {
            if (!TryGetMediaSnapshot(out AudioMediaTimelineSnapshot snapshot)) return false;

            UpdateMediaDiagnostics(snapshot);
            if (IsRecentSignal(snapshot))
            {
                BindMediaClock(
                    snapshot.SignalStartPositionSeconds,
                    snapshot.SignalGeneration,
                    snapshot.DiscontinuityGeneration,
                    fallback: false);
                return true;
            }

            ArmMediaClock(
                snapshot.LogicalPositionSeconds,
                snapshot.SignalGeneration,
                timestampUtc,
                snapshot.DiscontinuityGeneration,
                snapshot.AnalyserAvailable);
            return true;
        }

        private void ArmMediaClock(
            double lifecycleBaselineSeconds,
            int signalGeneration,
            DateTime timestampUtc,
            int discontinuityGeneration,
            bool analyserAvailable)
        {
            _mediaClockArmed = true;
            _mediaClockBound = false;
            _mediaClockFallback = false;
            _mediaLifecycleBaselineSeconds = Math.Max(0d, lifecycleBaselineSeconds);
            _mediaArmedSignalGeneration = Math.Max(0, signalGeneration);
            _lastMediaDiscontinuityGeneration = Math.Max(0, discontinuityGeneration);
            _mediaSignalDeadlineUtc = timestampUtc + BrowserSignalWait;
            _mediaAnalyserAvailable = analyserAvailable;
            BrowserSignalTimedOut = false;
            _audioStartUtc = null;
            _rawPlayheadBaseSeconds = 0d;
        }

        private void BindMediaClock(
            double baselineSeconds,
            int signalGeneration,
            int discontinuityGeneration,
            bool fallback)
        {
            _mediaClockArmed = false;
            _mediaClockBound = true;
            _mediaClockFallback = fallback;
            _mediaBaselineSeconds = Math.Max(0d, baselineSeconds);
            _lastMediaPositionSeconds = Math.Max(_mediaBaselineSeconds, _lastMediaPositionSeconds);
            _mediaSignalGeneration = Math.Max(0, signalGeneration);
            _lastMediaDiscontinuityGeneration = Math.Max(0, discontinuityGeneration);
            _mediaDiscontinuityPending = false;
            _audioStartUtc = null;
            _rawPlayheadBaseSeconds = 0d;
        }

        private bool TryGetMediaTarget(out double targetSeconds)
        {
            targetSeconds = 0d;
            if (!TryGetMediaSnapshot(out AudioMediaTimelineSnapshot snapshot))
            {
                if (!_mediaClockBound) return false;
                targetSeconds = _timelineBaseSeconds +
                                Math.Max(0d, _lastMediaPositionSeconds - _mediaBaselineSeconds);
                return true;
            }

            UpdateMediaDiagnostics(snapshot);
            if (_mediaClockArmed)
            {
                bool isNextSignal = snapshot.HasSignalStart &&
                                    (snapshot.SignalGeneration > _mediaArmedSignalGeneration ||
                                     IsRecentSignal(snapshot));
                if (isNextSignal)
                {
                    BindMediaClock(
                        snapshot.SignalStartPositionSeconds,
                        snapshot.SignalGeneration,
                        snapshot.DiscontinuityGeneration,
                        fallback: false);
                }
                else if (_utcNow() >= _mediaSignalDeadlineUtc)
                {
                    BrowserSignalTimedOut = true;
                    BrowserFallbackCount++;
                    BindMediaClock(
                        _mediaLifecycleBaselineSeconds,
                        snapshot.SignalGeneration,
                        snapshot.DiscontinuityGeneration,
                        fallback: true);
                }
                else
                {
                    return false;
                }
            }

            if (snapshot.DiscontinuityGeneration != _lastMediaDiscontinuityGeneration)
            {
                _mediaDiscontinuityPending = true;
                _lastMediaDiscontinuityGeneration = snapshot.DiscontinuityGeneration;
            }

            _lastMediaPositionSeconds = Math.Max(_lastMediaPositionSeconds, snapshot.LogicalPositionSeconds);
            targetSeconds = _timelineBaseSeconds +
                            Math.Max(0d, _lastMediaPositionSeconds - _mediaBaselineSeconds);
            return snapshot.State != AudioTimelinePlaybackState.Disposed;
        }

        private void UpdateMediaDiagnostics(in AudioMediaTimelineSnapshot snapshot)
        {
            _mediaAnalyserAvailable = snapshot.AnalyserAvailable;
            _mediaStallCount = snapshot.StallCount;
            _mediaElementReplacementCount = snapshot.ElementReplacementCount;
        }

        private void CapturePendingMediaBaseline(DateTime timestampUtc)
        {
            _hasPendingMediaCapability = false;
            _hasPendingExactMediaBaseline = false;
            if (!TryGetMediaSnapshot(out AudioMediaTimelineSnapshot snapshot)) return;

            _hasPendingMediaCapability = true;
            _pendingMediaLifecycleBaselineSeconds = snapshot.LogicalPositionSeconds;
            _pendingMediaSignalGeneration = snapshot.SignalGeneration;
            _pendingMediaDiscontinuityGeneration = snapshot.DiscontinuityGeneration;
            _pendingMediaAnalyserAvailable = snapshot.AnalyserAvailable;
            if (IsRecentSignal(snapshot))
            {
                _hasPendingExactMediaBaseline = true;
                _pendingMediaBaselineSeconds = snapshot.SignalStartPositionSeconds;
            }

            _pendingAudioStartUtc = timestampUtc;
        }

        private static bool IsRecentSignal(in AudioMediaTimelineSnapshot snapshot) =>
            snapshot.HasSignalStart &&
            snapshot.LogicalPositionSeconds >= snapshot.SignalStartPositionSeconds &&
            snapshot.LogicalPositionSeconds - snapshot.SignalStartPositionSeconds <= RecentBrowserSignalSeconds;

        private bool TryGetMediaSnapshot(out AudioMediaTimelineSnapshot snapshot)
        {
            AudioMediaTimelineSnapshot? value = _mediaSnapshotProvider?.Invoke();
            if (value.HasValue && value.Value.IsValid)
            {
                snapshot = value.Value;
                return true;
            }

            snapshot = default;
            return false;
        }

        private void ResetActiveMediaClock()
        {
            _mediaClockArmed = false;
            _mediaClockBound = false;
            _mediaClockFallback = false;
            _mediaBaselineSeconds = 0d;
            _mediaLifecycleBaselineSeconds = 0d;
            _lastMediaPositionSeconds = 0d;
            _mediaArmedSignalGeneration = 0;
            _mediaSignalGeneration = 0;
            _lastMediaDiscontinuityGeneration = 0;
            _mediaSignalDeadlineUtc = default;
            _mediaDiscontinuityPending = false;
            _mediaAnalyserAvailable = false;
            _mediaStallCount = 0;
            _mediaElementReplacementCount = 0;
            BrowserSignalTimedOut = false;
        }

        private void ResetMediaClock()
        {
            ResetActiveMediaClock();
            _hasPendingMediaCapability = false;
            _hasPendingExactMediaBaseline = false;
            _pendingMediaBaselineSeconds = 0d;
            _pendingMediaLifecycleBaselineSeconds = 0d;
            _pendingMediaSignalGeneration = 0;
            _pendingMediaDiscontinuityGeneration = 0;
            _pendingMediaAnalyserAvailable = false;
        }

        private void ClearPendingAudioStart()
        {
            _pendingAudioOwner = default;
            _hasPendingSampleBaseline = false;
            _pendingSampleBaselineFrame = 0;
            _pendingSampleRate = 0;
            _pendingSampleFormatGeneration = 0;
            _pendingSignalGeneration = 0;
            _pendingDiscontinuityGeneration = 0;
            _hasPendingMediaCapability = false;
            _hasPendingExactMediaBaseline = false;
            _pendingMediaBaselineSeconds = 0d;
            _pendingMediaLifecycleBaselineSeconds = 0d;
            _pendingMediaSignalGeneration = 0;
            _pendingMediaDiscontinuityGeneration = 0;
            _pendingMediaAnalyserAvailable = false;
            _pendingAudioStartUtc = null;
        }

        private void CapturePendingAudioBaseline(DateTime timestampUtc)
        {
            if (!_pendingAudioOwner.HasOwner) return;

            _pendingAudioStartUtc = timestampUtc;
            CapturePendingMediaBaseline(timestampUtc);
            _hasPendingSampleBaseline = false;
            if (_sampleClockBound && _sampleRate > 0)
            {
                // Audio may begin before its data-channel lifecycle packet. Preserve the baseline
                // captured by the real audio-start event instead of rebasing to lifecycle arrival.
                _hasPendingSampleBaseline = true;
                _pendingSampleBaselineFrame = _sampleBaselineFrame;
                _pendingSampleRate = _sampleRate;
                _pendingSampleFormatGeneration = _sampleFormatGeneration;
                _pendingSignalGeneration = _sampleSignalGeneration;
                _pendingDiscontinuityGeneration = _lastDiscontinuityGeneration;
                return;
            }

            if (!TryGetSampleSnapshot(out AudioTimelineSnapshot snapshot)) return;

            _hasPendingSampleBaseline = true;
            _pendingSampleBaselineFrame = SelectSampleBaseline(snapshot);
            _pendingSampleRate = snapshot.SampleRate;
            _pendingSampleFormatGeneration = snapshot.FormatGeneration;
            _pendingSignalGeneration = snapshot.SignalGeneration;
            _pendingDiscontinuityGeneration = snapshot.DiscontinuityGeneration;
        }

        private bool TryGetSampleSnapshot(out AudioTimelineSnapshot snapshot)
        {
            AudioTimelineSnapshot? value = _sampleSnapshotProvider?.Invoke();
            if (value.HasValue && value.Value.IsValid)
            {
                snapshot = value.Value;
                return true;
            }

            snapshot = default;
            return false;
        }

        private static bool OwnersMatch(LipSyncTimelineOwner a, LipSyncTimelineOwner b)
            => LipSyncTimelineAssembler.OwnersMatch(a, b);

        private static long SelectSampleBaseline(in AudioTimelineSnapshot snapshot) =>
            snapshot.HasSignalStart
                ? Math.Min(snapshot.SignalStartAbsoluteSourceFrame, snapshot.AbsoluteSourceFrame)
                : snapshot.AbsoluteSourceFrame;
    }
}
