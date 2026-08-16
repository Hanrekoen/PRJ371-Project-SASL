using System;
using System.Collections.Generic;

namespace Convai.Modules.LipSync
{
    /// <summary>
    ///     Pure C# lip sync playback engine. Transport, clock, and output agnostic.
    ///     Receives timestamped frames via FeedFrames and produces interpolated values via Tick.
    ///     Uses a single runtime path with zero allocations in the hot path and Catmull-Rom interpolation.
    ///     Playback is gated by an external audio-start signal to keep output aligned with audible speech.
    /// </summary>
    public sealed class LipSyncPlaybackEngine
    {
        private readonly FrameRingBuffer _buffer = new();
        private readonly FadeController _fade = new();
        private bool _audioPlaybackStarted;

        private LipSyncEngineConfig _config = LipSyncEngineConfig.Default;

        private float[] _sampledValues = Array.Empty<float>();
        private bool _streamEndNotified;
        private bool _sendAheadTimeline;
        private float[] _fadeInSource = Array.Empty<float>();
        private double _fadeInStartElapsed;
        private bool _fadeInActive;
        private float[] _timelineRebaseSource = Array.Empty<float>();
        private double _timelineRebaseStartElapsed;
        private float _timelineRebaseDuration;
        private bool _timelineRebaseActive;

        public LipSyncPlaybackEngine() { }

        public LipSyncPlaybackEngine(LipSyncEngineConfig config)
        {
            _config = config;
        }

        public PlaybackState State { get; private set; } = PlaybackState.Idle;

        public float[] OutputValues { get; private set; } = Array.Empty<float>();

        public IReadOnlyList<string> ChannelNames => _buffer.ChannelNames;
        public int ChannelCount => _buffer.ChannelCount;
        public float BufferedDuration => _buffer.Duration;
        public float FrameRate { get; private set; } = 60f;

        public bool IsPlaying => State == PlaybackState.Playing || State == PlaybackState.Starving;
        public bool IsFadingOut => State == PlaybackState.FadingOut;

        /// <summary>Total duration of all frames ingested since stream start (not limited by ring buffer capacity).</summary>
        public float TotalIngressDuration { get; private set; }

        public event Action<PlaybackState, PlaybackState> StateChanged;

        public void Configure(LipSyncEngineConfig config) => _config = config;

        /// <summary>Begin a new stream. Resets all playback state.</summary>
        /// <param name="sendAheadTimeline">
        ///     When true, frames arrive far ahead of playback on an absolute timeline and the initial
        ///     buffering headroom wait is bypassed so the mouth opens with the first audible sample.
        /// </param>
        public void BeginStream(IReadOnlyList<string> channelNames, float frameRate, bool sendAheadTimeline = false)
        {
            FullReset();
            FrameRate = Math.Max(1f, frameRate);
            _sendAheadTimeline = sendAheadTimeline;
            _buffer.SetChannelLayout(channelNames);

            int channelCount = channelNames?.Count ?? 0;
            EnsureOutputArrays(channelCount);

            TransitionTo(PlaybackState.Buffering);
        }

        /// <summary>Feed new frames into the buffer. Timestamps are auto-computed from frame rate.</summary>
        public void FeedFrames(float[][] frames) => FeedFramesAt(frames, TotalIngressDuration);

        /// <summary>
        ///     Feed frames whose first frame sits at an explicit timeline position (seconds).
        ///     Used by the indexed send-ahead path where position is start_frame_index / fps,
        ///     so playback time reflects the server timeline instead of arrival order.
        /// </summary>
        public void FeedFramesAt(float[][] frames, double startTimeSeconds)
        {
            if (frames == null || frames.Length == 0) return;

            if (State == PlaybackState.Idle) return;

            if (State == PlaybackState.FadingOut) return;

            float startTime = (float)Math.Max(0d, startTimeSeconds);
            _buffer.AppendFrames(frames, startTime, FrameRate, _config.MaxBufferedSeconds, _config.RetainFutureFrames);
            TotalIngressDuration = Math.Max(TotalIngressDuration, startTime + (frames.Length / FrameRate));

            EnsureOutputArrays(_buffer.ChannelCount);
        }

        /// <summary>
        ///     Discards buffered frames past the given timeline position and marks the stream ended,
        ///     so playback drains the kept tail and fades at the boundary. Used when the server cancels
        ///     a turn but audio through that position was already released.
        /// </summary>
        public void TruncateAfter(double timelineSeconds)
        {
            if (State == PlaybackState.Idle || State == PlaybackState.FadingOut) return;

            float cutoff = (float)Math.Max(0d, timelineSeconds);
            _buffer.TruncateAfter(cutoff);
            TotalIngressDuration = Math.Min(TotalIngressDuration, cutoff);
            _streamEndNotified = true;

            if (!_buffer.HasContent) HandleBufferExhausted();
        }

        /// <summary>Signal that no more frames will arrive for this stream.</summary>
        public void NotifyStreamEnd() => _streamEndNotified = true;

        /// <summary>
        ///     Signals that remote audio playback has started (for example, CharacterAudioPlaybackStateChanged).
        ///     Unlocks the Buffering -> Playing transition so lip sync starts with actual audio output.
        ///     If a fade-out was started by <see cref="NotifyAudioPlaybackStopped" />, cancels it and resumes
        ///     sampling when buffer data is still available (quick stop/start between utterances).
        /// </summary>
        public void NotifyAudioPlaybackStarted()
        {
            _audioPlaybackStarted = true;
            if (State != PlaybackState.FadingOut) return;

            _fade.Reset();
            if (_buffer.HasContent)
                TransitionTo(PlaybackState.Playing);
            else
                TransitionTo(PlaybackState.Buffering);
        }

        /// <summary>
        ///     Signals that remote audio playback has stopped. Begins smooth fade-out so trailing lip-sync
        ///     buffer does not continue after audible speech ends.
        /// </summary>
        public void NotifyAudioPlaybackStopped()
        {
            if (State == PlaybackState.Idle || State == PlaybackState.FadingOut) return;

            StopSmooth();
        }

        /// <summary>
        /// Blends from the currently displayed pose after the source-sample clock jumps over
        /// discarded audio. Audio remains authoritative; only the visible transition is softened.
        /// </summary>
        public void BeginTimelineRebaseBlend(double newElapsed, float durationSeconds = 0.08f)
        {
            if (State == PlaybackState.Idle || State == PlaybackState.FadingOut || OutputValues.Length == 0) return;

            if (_timelineRebaseSource.Length != OutputValues.Length)
                _timelineRebaseSource = new float[OutputValues.Length];
            Array.Copy(OutputValues, _timelineRebaseSource, OutputValues.Length);
            _timelineRebaseStartElapsed = Math.Max(0d, newElapsed + _config.TimeOffsetSeconds);
            _timelineRebaseDuration = Math.Clamp(durationSeconds, 0.05f, 0.08f);
            _timelineRebaseActive = true;
        }

        /// <summary>
        ///     Advances playback by one frame. Call once per LateUpdate.
        ///     Returns true if output values were updated this tick.
        /// </summary>
        /// <param name="clockElapsed">Elapsed seconds from the playback clock.</param>
        /// <param name="deltaTime">Frame delta time (for optional smoothing and fade).</param>
        public bool Tick(double clockElapsed, float deltaTime)
        {
            if (State == PlaybackState.FadingOut) return TickFadeOut(deltaTime);
            if (State == PlaybackState.Idle) return false;

            if (!_buffer.HasContent)
            {
                HandleBufferExhausted();
                return false;
            }

            if (!_audioPlaybackStarted)
            {
                TransitionTo(PlaybackState.Buffering);
                return false;
            }

            double elapsed = Math.Max(0d, clockElapsed + _config.TimeOffsetSeconds);
            float endTime = _buffer.EndTime;

            if (elapsed > endTime)
            {
                HandleBufferExhausted();
                elapsed = endTime;
            }
            else if (State == PlaybackState.Starving || State == PlaybackState.Buffering)
            {
                float headroom = endTime - (float)elapsed;
                if (State == PlaybackState.Starving)
                {
                    if (headroom >= _config.MinResumeHeadroomSeconds) TransitionTo(PlaybackState.Playing);
                }
                // Send-ahead streams already hold seconds of future frames; waiting for headroom here
                // only delays mouth-open past audible audio.
                else if (headroom < _config.MinResumeHeadroomSeconds && !_streamEndNotified && !_sendAheadTimeline)
                {
                    return false;
                }
            }

            bool wasBuffering = State == PlaybackState.Buffering;
            if (wasBuffering) CaptureFadeInSource();

            bool sampled = SampleAtTime(elapsed, deltaTime);

            if (sampled && _config.RetainFutureFrames)
                _buffer.PruneBefore((float)(elapsed - (2d / FrameRate)));

            if (sampled && wasBuffering)
            {
                BeginFadeIn(elapsed);
                TransitionTo(PlaybackState.Playing);
            }

            if (sampled)
            {
                ApplyFadeIn(elapsed);
                ApplyTimelineRebaseBlend(elapsed);
            }

            return sampled;
        }

        /// <summary>Snapshot the currently displayed pose so the first played frames can ramp from it.</summary>
        private void CaptureFadeInSource()
        {
            int channelCount = OutputValues.Length;
            if (channelCount <= 0) return;

            if (_fadeInSource.Length != channelCount) _fadeInSource = new float[channelCount];
            Array.Copy(OutputValues, _fadeInSource, channelCount);
        }

        private void BeginFadeIn(double elapsed)
        {
            if (_config.FadeInDuration <= 0f) return;

            _fadeInStartElapsed = elapsed;
            _fadeInActive = true;
        }

        /// <summary>
        ///     Blends output from the captured pre-playback pose toward the sampled frames over
        ///     FadeInDuration of timeline progress, removing the first-frame pop at playback start.
        /// </summary>
        private void ApplyFadeIn(double elapsed)
        {
            if (!_fadeInActive) return;

            float alpha = Math.Clamp((float)((elapsed - _fadeInStartElapsed) / _config.FadeInDuration), 0f, 1f);
            if (alpha >= 1f)
            {
                _fadeInActive = false;
                return;
            }

            int count = Math.Min(OutputValues.Length, _fadeInSource.Length);
            for (int i = 0; i < count; i++)
                OutputValues[i] = _fadeInSource[i] + ((OutputValues[i] - _fadeInSource[i]) * alpha);
        }

        private void ApplyTimelineRebaseBlend(double elapsed)
        {
            if (!_timelineRebaseActive) return;

            float alpha = Math.Clamp((float)((elapsed - _timelineRebaseStartElapsed) / _timelineRebaseDuration), 0f, 1f);
            if (alpha >= 1f)
            {
                _timelineRebaseActive = false;
                return;
            }

            int count = Math.Min(OutputValues.Length, _timelineRebaseSource.Length);
            for (int i = 0; i < count; i++)
                OutputValues[i] = _timelineRebaseSource[i] +
                                  ((OutputValues[i] - _timelineRebaseSource[i]) * alpha);
        }

        /// <summary>Immediately stop and zero output.</summary>
        public void Stop()
        {
            PlaybackState prev = State;
            FullReset();
            if (prev != PlaybackState.Idle) TransitionTo(PlaybackState.Idle);
        }

        /// <summary>Begin smooth fade-out from current values.</summary>
        public void StopSmooth()
        {
            if (State == PlaybackState.Idle || State == PlaybackState.FadingOut) return;

            _fade.Begin(OutputValues, _config.FadeOutDuration);
            TransitionTo(PlaybackState.FadingOut);
        }

        /// <summary>
        ///     Injects fade target values after a fade-out has begun.
        ///     Instead of fading to zero, the engine will fade toward these values.
        ///     Values are in normalized 0-1 source space.
        /// </summary>
        public void SetFadeTargets(float[] targets)
        {
            if (State != PlaybackState.FadingOut || targets == null || targets.Length == 0) return;

            _fade.Begin(OutputValues, _config.FadeOutDuration - (_fade.Progress * _config.FadeOutDuration), targets);
        }

        /// <summary>Remaining playback time based on buffer end minus logical elapsed.</summary>
        public float GetRemainingSeconds(double clockElapsed)
        {
            if (State == PlaybackState.Idle || State == PlaybackState.FadingOut) return 0f;

            double elapsed = Math.Max(0d, clockElapsed + _config.TimeOffsetSeconds);
            return Math.Max(0f, _buffer.EndTime - (float)elapsed);
        }

        /// <summary>Current headroom: how far the buffer end is ahead of the logical playback position.</summary>
        public float GetHeadroomSeconds(double clockElapsed)
        {
            if (State == PlaybackState.Idle || State == PlaybackState.FadingOut) return 0f;

            double elapsed = Math.Max(0d, clockElapsed + _config.TimeOffsetSeconds);
            return (float)(_buffer.EndTime - elapsed);
        }

        private bool SampleAtTime(double elapsed, float deltaTime)
        {
            int channelCount = _buffer.ChannelCount;
            if (channelCount <= 0) return false;

            EnsureOutputArrays(channelCount);

            bool usesSmoothing = _config.SmoothingFactor > 0f;

            if (!_buffer.TryGetFrameWindow(elapsed,
                    out float[] p0, out float[] p1, out float[] p2, out float[] p3,
                    out float alpha))
                return false;

            if (usesSmoothing)
            {
                FrameSampler.EvaluateCatmullRom(p0, p1, p2, p3, alpha, _sampledValues, channelCount);
                FrameSampler.ApplyTemporalSmoothing(_sampledValues, OutputValues, _config.SmoothingFactor, deltaTime,
                    channelCount);
            }
            else
                FrameSampler.EvaluateCatmullRom(p0, p1, p2, p3, alpha, OutputValues, channelCount);

            return true;
        }

        private void HandleBufferExhausted()
        {
            if (_streamEndNotified)
            {
                _fade.Begin(OutputValues, _config.FadeOutDuration);
                TransitionTo(PlaybackState.FadingOut);
                return;
            }

            if (State != PlaybackState.Starving) TransitionTo(PlaybackState.Starving);
        }

        private bool TickFadeOut(float deltaTime)
        {
            bool stillFading = _fade.Tick(deltaTime, OutputValues);
            if (!stillFading)
            {
                FullReset();
                TransitionTo(PlaybackState.Idle);
            }

            return true;
        }

        private void EnsureOutputArrays(int channelCount)
        {
            if (channelCount <= 0) return;

            if (OutputValues.Length != channelCount) OutputValues = new float[channelCount];

            if (_config.SmoothingFactor > 0f && _sampledValues.Length != channelCount)
                _sampledValues = new float[channelCount];
        }

        private void FullReset()
        {
            _buffer.Clear();
            _fade.Reset();
            FrameRate = 60f;
            TotalIngressDuration = 0f;
            _streamEndNotified = false;
            _audioPlaybackStarted = false;
            _sendAheadTimeline = false;
            _fadeInActive = false;
            _fadeInStartElapsed = 0d;
            _timelineRebaseActive = false;
            _timelineRebaseStartElapsed = 0d;
            _timelineRebaseDuration = 0f;

            if (OutputValues.Length > 0) Array.Clear(OutputValues, 0, OutputValues.Length);

            if (_sampledValues.Length > 0) Array.Clear(_sampledValues, 0, _sampledValues.Length);
        }

        private void TransitionTo(PlaybackState next)
        {
            if (State == next) return;

            PlaybackState prev = State;
            State = next;
            StateChanged?.Invoke(prev, next);
        }
    }
}
