using System;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Runtime.Logging;
using Convai.Runtime.Room;
using UnityEngine;
using ILogger = Convai.Domain.Logging.ILogger;

namespace Convai.Modules.LipSync
{
    internal sealed class LipSyncRuntimeController : IDisposable
    {
        private const string LogPrefix = "[Convai LipSync Runtime]";
        // Drift correction between the visual clock and the measured audio playhead:
        // errors inside the deadband are ignored; errors below the snap threshold are corrected
        // proportionally (a fraction per tick, capped so each step is imperceptible); larger
        // errors re-base the clock in one step. Proportional correction is required because
        // WebRTC micro-underruns (20-90 ms) freeze the playhead in small increments that a
        // fixed slow slew cannot keep up with, accumulating into visible backward snaps.
        private const double DriftDeadbandSeconds = 0.02;
        private const double DriftSnapThresholdSeconds = 0.25;
        private const double DriftProportionalGain = 0.15;
        private const double DriftMaxStepSeconds = 0.008;
        private const float AudioStallPauseSeconds = 0.08f;

        private readonly IPlaybackClock _injectedClock;
        private string _boundCharacterId;
        private ConvaiLipSyncBridge _bridge;

        private LipSyncRuntimeConfig _config;

        private IEventHub _eventHub;
        private ILogger _logger;
        private bool _isPlaybackClockStarted;
        private IPlaybackClock _clock;
        private IConvaiRoomAudioService _roomAudioService;
        private SkinnedMeshBlendshapeSink _sink;
        private double _lastDriftTarget;
        private float _driftTargetFrozenSeconds;
        private bool _clockPausedForAudioStall;
        private float _driftCumulativeCorrectionMs;
        private float? _appliedTimeOffsetOverride;

        public LipSyncRuntimeController(IPlaybackClock playbackClock = null)
        {
            _injectedClock = playbackClock;
        }

        public bool IsInitialized { get; private set; }

        public bool IsPlaying => Engine?.IsPlaying ?? false;
        public bool IsFadingOut => Engine?.IsFadingOut ?? false;
        public PlaybackState EngineState => Engine?.State ?? PlaybackState.Idle;
        public LipSyncPlaybackEngine Engine { get; private set; }

        public IPlaybackClock CurrentClock => _clock;

        public void EnsureInitialized(Component context, LipSyncRuntimeConfig config,
            ConvaiLipSyncMapAsset effectiveMapping)
        {
            if (!IsInitialized)
            {
                Engine = new LipSyncPlaybackEngine(config.ToEngineConfig());
                Engine.StateChanged += OnEngineStateChanged;
                _sink = new SkinnedMeshBlendshapeSink(context);
                _clock = _injectedClock ?? LipSyncClockResolver.Create();
                IsInitialized = true;
                if (CanLogDebug)
                    LogDebug(
                        $"Initialized: profile='{config.ProfileId}', targetMeshes={config.TargetMeshes?.Count ?? 0}, clock='{_clock?.GetType().Name ?? "null"}'.");
            }

            Reconfigure(config, effectiveMapping);
        }

        public void Reconfigure(LipSyncRuntimeConfig config, ConvaiLipSyncMapAsset effectiveMapping)
        {
            if (!IsInitialized) return;

            bool profileChanged = _config.ProfileId != config.ProfileId;
            _config = config;
            Engine.Configure(config.ToEngineConfig());
            _sink?.Initialize(config.TargetMeshes, effectiveMapping);
            if (CanLogDebug)
                LogDebug(
                    $"Reconfigure: profile='{config.ProfileId}', profileChanged={profileChanged}, deliverAhead={config.DeliverChunksAhead}, targetMeshes={config.TargetMeshes?.Count ?? 0}, mapping='{effectiveMapping?.name ?? "null"}'.");

            if (profileChanged) RecreateBridgeForCurrentProfile();
        }

        public void SetRoomAudioService(IConvaiRoomAudioService roomAudioService)
        {
            if (ReferenceEquals(_roomAudioService, roomAudioService)) return;

            _roomAudioService = roomAudioService;
            RecreateBridgeForCurrentProfile();
        }

        public void Bind(IEventHub eventHub, string characterId, ILogger logger)
        {
            _eventHub = eventHub;
            _logger = logger;
            _boundCharacterId = characterId?.Trim() ?? string.Empty;
            if (CanLogDebug)
                LogDebug(
                    $"Bind requested: initialized={IsInitialized}, eventHubNull={_eventHub == null}, characterId='{_boundCharacterId}', profile='{_config.ProfileId}'.");

            if (!IsInitialized || _eventHub == null || string.IsNullOrWhiteSpace(_boundCharacterId)) return;

            EnsureBridge();
            _bridge.Bind(_eventHub, _boundCharacterId);
        }

        public void UnbindAndReset()
        {
            if (CanLogDebug)
                LogDebug($"UnbindAndReset: characterId='{_boundCharacterId}', state='{Engine?.State.ToString() ?? "null"}'.");
            _bridge?.Unbind();
            Engine?.Stop();
            _clock?.Reset();
            _isPlaybackClockStarted = false;
            ResetDriftCorrectionState();
        }

        public void Tick(float deltaTime)
        {
            if (!IsInitialized || Engine == null) return;

            if (_clock == null) return;

            float dt = Mathf.Clamp(deltaTime, LipSyncConstants.MinDeltaTime, LipSyncConstants.MaxDeltaTimeForFade);

            _bridge?.Tick(dt);

            double authoritativeTarget = 0d;
            bool authoritativeClock = _bridge != null &&
                                      _bridge.TryGetAuthoritativeTimelineTarget(out authoritativeTarget);
            bool updated;
            if (authoritativeClock)
            {
                // Native source samples and WebGL media time are authoritative response clocks.
                // Sampling either directly removes the second visual clock and cumulative drift.
                _isPlaybackClockStarted = true;
                double smoothingCompensation = FrameSampler.GetTemporalSmoothingCompensationSeconds(
                    _config.SmoothingFactor);
                double compensatedTarget = authoritativeTarget + smoothingCompensation;
                if (_bridge.ConsumeAuthoritativeClockDiscontinuity())
                    Engine.BeginTimelineRebaseBlend(compensatedTarget);

                updated = Engine.Tick(compensatedTarget, dt);
            }
            else
            {
                // Legacy measured-playhead and wall-clock paths retain the compatibility clock.
                TickDriftCorrection(dt);
                updated = Engine.Tick(GetClockElapsedSeconds(), dt);
            }

            _sink?.SetSpeechActive(Engine.IsPlaying || Engine.IsFadingOut);

            if (updated) _sink.Apply(Engine.OutputValues, Engine.ChannelNames);

            // Runs regardless of the monitor's Enabled flag: an override must be restored even
            // after the monitor is switched off, or the engine keeps the tuning offset forever.
            ApplyTimeOffsetOverrideIfChanged();

            if (LipSyncDriftMonitor.Enabled) RecordDriftDiagnostics();
        }

        /// <summary>Applies or restores the drift-monitor's live A/V offset override.</summary>
        private void ApplyTimeOffsetOverrideIfChanged()
        {
            float? requestedOverride = LipSyncDriftMonitor.TimeOffsetOverrideSeconds;
            if (Nullable.Equals(requestedOverride, _appliedTimeOffsetOverride)) return;

            _appliedTimeOffsetOverride = requestedOverride;
            LipSyncEngineConfig baseConfig = _config.ToEngineConfig();
            Engine.Configure(requestedOverride.HasValue
                ? new LipSyncEngineConfig(
                    baseConfig.FadeOutDuration,
                    baseConfig.SmoothingFactor,
                    requestedOverride.Value,
                    baseConfig.MaxBufferedSeconds,
                    baseConfig.MinResumeHeadroomSeconds,
                    baseConfig.RetainFutureFrames,
                    baseConfig.FadeInDuration)
                : baseConfig);
            if (CanLogDebug)
                LogDebug(
                    $"Drift monitor: TimeOffset override {(requestedOverride.HasValue ? $"{requestedOverride.Value * 1000f:F0}ms" : "cleared")}.");
        }

        /// <summary>Feeds the drift monitor one sample per frame. Only runs while the monitor is enabled.</summary>
        private void RecordDriftDiagnostics()
        {
            LipSyncDriftMonitor.TimeSource ??= () => Time.realtimeSinceStartup;

            if (string.IsNullOrEmpty(_boundCharacterId) || _bridge == null) return;

            double elapsed = GetPlaybackElapsedSeconds();
            bool audioActive = _bridge.TryGetAudioTimelineTarget(out double target);
            float errorMs = audioActive ? (float)((target - elapsed) * 1000d) : 0f;
            LipSyncDriftMonitor.RecordSample(_boundCharacterId, new LipSyncDriftSample(
                Time.realtimeSinceStartup,
                target,
                elapsed,
                errorMs,
                _driftCumulativeCorrectionMs,
                Engine.BufferedDuration,
                Engine.IsPlaying ? Engine.GetHeadroomSeconds(elapsed) : 0f,
                Engine.State,
                audioActive));
        }

        /// <summary>
        ///     Continuously re-anchors the visual clock to the measured audio playhead: the target
        ///     position comes from audio actually rendered to the device, so the mouth stays locked
        ///     to what is audible over long responses, after stalls, and across device clock skew.
        /// </summary>
        private void TickDriftCorrection(float dt)
        {
            if (!_isPlaybackClockStarted || _bridge == null) return;
            if (Engine.State != PlaybackState.Playing && Engine.State != PlaybackState.Starving) return;

            IPlaybackClock clock = _clock;
            if (clock == null) return;

            if (!_bridge.TryGetAudioTimelineTarget(out double target)) return;

            // A visual clock far ahead of the rendered-audio playhead means the time source kept
            // running while the audio device did not (editor focus loss, app pause). Snap back
            // before the engine samples with the jumped clock, or it consumes and truncates every
            // frame the audio has not reached yet. Runs even while the target is frozen: on the
            // first tick after refocus the audio has not resumed rendering, which is exactly when
            // the clamp must happen.
            double elapsedAheadOfAudio = GetClockElapsedSeconds() - target;
            if (elapsedAheadOfAudio > DriftSnapThresholdSeconds)
            {
                clock.Rebase(target);
                _driftCumulativeCorrectionMs += (float)(-elapsedAheadOfAudio * 1000d);
                if (CanLogDebug)
                    LogDebug(
                        $"Drift: visual clock ran {elapsedAheadOfAudio:F3}s ahead of the audio playhead (focus loss or pause); snapped back to {target:F3}s.");
                if (LipSyncDriftMonitor.Enabled)
                    LipSyncDriftMonitor.RecordEvent(_boundCharacterId,
                        $"forward jump clamp: -{elapsedAheadOfAudio * 1000d:F0}ms");
            }

            if (Engine.State != PlaybackState.Playing) return;

            bool targetAdvanced = target > _lastDriftTarget + 1e-6;
            if (!targetAdvanced)
            {
                // Audio playhead frozen (device underrun / network stall): stop the visual clock
                // instead of letting it run ahead of silence.
                _driftTargetFrozenSeconds += dt;
                if (_driftTargetFrozenSeconds >= AudioStallPauseSeconds && !_clockPausedForAudioStall)
                {
                    _clockPausedForAudioStall = true;
                    clock.Pause();
                    if (CanLogDebug)
                        LogDebug($"Drift: audio playhead stalled at {target:F3}s; visual clock paused.");
                    if (LipSyncDriftMonitor.Enabled)
                        LipSyncDriftMonitor.RecordEvent(_boundCharacterId, $"stall: clock paused @{target:F2}s");
                }

                return;
            }

            _lastDriftTarget = target;
            _driftTargetFrozenSeconds = 0f;

            if (_clockPausedForAudioStall)
            {
                _clockPausedForAudioStall = false;
                double pausedElapsed = GetClockElapsedSeconds();
                clock.Resume();
                clock.Rebase(target);
                _driftCumulativeCorrectionMs += (float)((target - pausedElapsed) * 1000d);
                if (CanLogDebug)
                    LogDebug($"Drift: audio playhead resumed; visual clock re-anchored to {target:F3}s.");
                if (LipSyncDriftMonitor.Enabled)
                    LipSyncDriftMonitor.RecordEvent(_boundCharacterId, $"stall over: rebase to {target:F2}s");
                return;
            }

            double elapsed = GetClockElapsedSeconds();
            double error = target - elapsed;
            double magnitude = Math.Abs(error);
            if (magnitude < DriftDeadbandSeconds) return;

            if (magnitude < DriftSnapThresholdSeconds)
            {
                double step = Math.Min(magnitude * DriftProportionalGain, DriftMaxStepSeconds) * Math.Sign(error);
                clock.Rebase(elapsed + step);
                _driftCumulativeCorrectionMs += (float)(step * 1000d);
                return;
            }

            clock.Rebase(target);
            _driftCumulativeCorrectionMs += (float)(error * 1000d);
            if (CanLogDebug)
                LogDebug($"Drift: visual clock snapped from {elapsed:F3}s to audio playhead {target:F3}s.");
            if (LipSyncDriftMonitor.Enabled)
                LipSyncDriftMonitor.RecordEvent(_boundCharacterId, $"snap: {error * 1000d:F0}ms");
        }

        public float GetTalkingTimeRemaining()
        {
            if (Engine == null || !Engine.IsPlaying) return 0f;

            return Engine.GetRemainingSeconds(GetPlaybackElapsedSeconds());
        }

        public float GetTalkingTimeElapsed()
        {
            if (Engine == null || !Engine.IsPlaying) return 0f;

            return Mathf.Max(0f, (float)GetPlaybackElapsedSeconds());
        }

        public float GetTotalBufferedDuration() => Engine?.BufferedDuration ?? 0f;

        public float GetTotalStreamDuration() => Engine?.TotalIngressDuration ?? 0f;

        public float GetHeadroom()
        {
            if (Engine == null || !Engine.IsPlaying) return 0f;

            return Engine.GetHeadroomSeconds(GetPlaybackElapsedSeconds());
        }

        private double GetPlaybackElapsedSeconds()
        {
            return _bridge != null && _bridge.TryGetAuthoritativeTimelineTarget(out double target)
                ? target
                : GetClockElapsedSeconds();
        }

        public BlendshapeSnapshot GetBlendshapeSnapshot()
        {
            if (Engine == null || Engine.OutputValues == null || Engine.ChannelNames == null) return default;

            return new BlendshapeSnapshot(Engine.OutputValues, Engine.ChannelNames);
        }

        public void Dispose()
        {
            if (Engine != null) Engine.StateChanged -= OnEngineStateChanged;

            _bridge?.Dispose();
            _bridge = null;
            _clock?.Reset();
        }

        private void OnEngineStateChanged(PlaybackState prev, PlaybackState next)
        {
            if (CanLogDebug)
                LogDebug(
                    $"Engine state: {prev} -> {next}, buffered={Engine?.BufferedDuration ?? 0f:F3}s, ingress={Engine?.TotalIngressDuration ?? 0f:F3}s, clock={GetClockElapsedSeconds():F3}s, clockStarted={_isPlaybackClockStarted}.");

            // A new stream must always get a fresh clock: audio-start events with no active stream
            // (residual tail of an interrupted turn) could otherwise start the clock early, and the
            // next stream would resume it seconds ahead of its own audio.
            if (prev == PlaybackState.Idle && next == PlaybackState.Buffering && _isPlaybackClockStarted)
            {
                _clock?.Reset();
                _isPlaybackClockStarted = false;
                LogDebug("Clock command queued: Reset because a new stream began; stale clock discarded.");
            }

            if (next == PlaybackState.Playing && prev == PlaybackState.Buffering)
                StartPlaybackClockIfNeeded();

            if (next == PlaybackState.Starving)
            {
                _clock?.Pause();
                LogDebug("Clock command queued: Pause because engine starved.");
            }

            if (next == PlaybackState.Playing && prev == PlaybackState.Starving)
            {
                _clock?.Resume();
                LogDebug("Clock command queued: Resume because engine recovered from starvation.");
            }

            if (next == PlaybackState.Idle)
            {
                _clock?.Reset();
                _isPlaybackClockStarted = false;
                _sink?.ResetToZero(Engine?.ChannelNames);
                LogDebug("Clock command queued: Reset because engine is idle; sink reset to zero.");
            }
        }

        private void OnPlaybackGateOpened()
        {
            LogDebug("Playback gate opened.");
            StartPlaybackClockIfNeeded();
        }

        private void OnPlaybackGateClosed()
        {
            _clock?.Reset();
            _isPlaybackClockStarted = false;
            LogDebug("Playback gate closed; clock reset queued.");
        }

        private void OnPlaybackGatePaused()
        {
            if (!_isPlaybackClockStarted)
            {
                LogDebug("Playback gate pause ignored: clock not started.");
                return;
            }

            _clock?.Pause();
            LogDebug("Playback gate paused; clock pause queued.");
        }

        private void OnPlaybackGateResumed()
        {
            if (_isPlaybackClockStarted)
            {
                _clock?.Resume();
                LogDebug("Playback gate resumed; clock resume queued.");
                return;
            }

            LogDebug("Playback gate resumed before clock start; starting clock.");
            StartPlaybackClockIfNeeded();
        }

        private void StartPlaybackClockIfNeeded()
        {
            if (_isPlaybackClockStarted)
            {
                LogDebug("Clock start skipped: already started.");
                return;
            }

            // No stream, nothing to clock; starting here would hand the next stream a stale elapsed.
            if (Engine == null || Engine.State == PlaybackState.Idle)
            {
                LogDebug("Clock start skipped: engine idle (no active stream).");
                return;
            }

            double startOffset = _bridge?.GetPlaybackStartElapsedSeconds() ?? 0d;
            if (startOffset > 0d)
                _clock?.StartClock(startOffset);
            else
                _clock?.StartClock();
            _isPlaybackClockStarted = true;
            ResetDriftCorrectionState();
            if (CanLogDebug)
                LogDebug($"Clock started at {startOffset:F3}s.");
            if (LipSyncDriftMonitor.Enabled)
                LipSyncDriftMonitor.RecordEvent(_boundCharacterId, $"clock start @{startOffset * 1000d:F0}ms");
        }

        private double GetClockElapsedSeconds() => Math.Max(0d, _clock?.ElapsedSeconds ?? 0d);

        private void ResetDriftCorrectionState()
        {
            _lastDriftTarget = 0d;
            _driftTargetFrozenSeconds = 0f;
            _clockPausedForAudioStall = false;
            _driftCumulativeCorrectionMs = 0f;
        }

        private void RecreateBridgeForCurrentProfile()
        {
            if (_bridge == null) return;

            _bridge.Dispose();
            _bridge = null;
            if (_eventHub != null && !string.IsNullOrWhiteSpace(_boundCharacterId))
            {
                EnsureBridge();
                _bridge.Bind(_eventHub, _boundCharacterId);
            }
        }

        private void EnsureBridge()
        {
            if (_bridge != null) return;

            if (Engine == null)
            {
                ConvaiLogger.Error("Runtime engine is not initialized. Bridge binding skipped.",
                    LogCategory.LipSync);
                return;
            }

            _bridge = new ConvaiLipSyncBridge(
                Engine,
                _config.ProfileId,
                _roomAudioService,
                _logger,
                OnPlaybackGateOpened,
                OnPlaybackGateClosed,
                OnPlaybackGatePaused,
                OnPlaybackGateResumed);
            if (CanLogDebug)
                LogDebug($"Bridge created: profile='{_config.ProfileId}', roomAudioServiceNull={_roomAudioService == null}.");
        }

        private bool CanLogDebug =>
            _logger?.IsEnabled(LogLevel.Debug, LogCategory.LipSync) ??
            LoggingConfig.IsDebugEnabled(LogCategory.LipSync);

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

    }
}
