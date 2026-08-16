using System;
using UnityEngine;
using LiveKit.Internal;
using LiveKit.Proto;
using System.Runtime.InteropServices;
using System.Threading;
using LiveKit.Internal.FFIClients.Requests;

namespace LiveKit
{
    /// <summary>Current health state of a remote audio stream's source-sample clock.</summary>
    public enum AudioStreamPlaybackState
    {
        Idle,
        Playing,
        Underrun,
        Disposed
    }

    /// <summary>
    /// Immutable, lock-free snapshot of source audio delivered through Unity's audio callback.
    /// AbsoluteSourceFrame advances for real PCM, including zero-valued PCM, and freezes on underrun.
    /// </summary>
    public readonly struct AudioStreamPlaybackSnapshot
    {
        public AudioStreamPlaybackSnapshot(
            long absoluteSourceFrame,
            int sampleRate,
            int channels,
            int formatGeneration,
            long bufferedFrames,
            AudioStreamPlaybackState state,
            long receivedFrames,
            long renderedFrames,
            long skippedFrames,
            long overflowCount,
            long underrunCount,
            long signalStartAbsoluteSourceFrame = -1,
            int signalGeneration = 0,
            long callbackSourceFrameStart = 0,
            int callbackSourceFrameCount = 0,
            double callbackDspTime = 0d,
            int discontinuityGeneration = 0)
        {
            AbsoluteSourceFrame = absoluteSourceFrame;
            SampleRate = sampleRate;
            Channels = channels;
            FormatGeneration = formatGeneration;
            BufferedFrames = bufferedFrames;
            State = state;
            ReceivedFrames = receivedFrames;
            RenderedFrames = renderedFrames;
            SkippedFrames = skippedFrames;
            OverflowCount = overflowCount;
            UnderrunCount = underrunCount;
            SignalStartAbsoluteSourceFrame = signalStartAbsoluteSourceFrame;
            SignalGeneration = signalGeneration;
            CallbackSourceFrameStart = callbackSourceFrameStart;
            CallbackSourceFrameCount = callbackSourceFrameCount;
            CallbackDspTime = callbackDspTime;
            DiscontinuityGeneration = discontinuityGeneration;
        }

        public long AbsoluteSourceFrame { get; }
        public int SampleRate { get; }
        public int Channels { get; }
        public int FormatGeneration { get; }
        public long BufferedFrames { get; }
        public AudioStreamPlaybackState State { get; }
        public long ReceivedFrames { get; }
        public long RenderedFrames { get; }
        public long SkippedFrames { get; }
        public long OverflowCount { get; }
        public long UnderrunCount { get; }
        public long SignalStartAbsoluteSourceFrame { get; }
        public int SignalGeneration { get; }
        public long CallbackSourceFrameStart { get; }
        public int CallbackSourceFrameCount { get; }
        public double CallbackDspTime { get; }
        public int DiscontinuityGeneration { get; }
        public bool HasSignalStart => SignalGeneration > 0 && SignalStartAbsoluteSourceFrame >= 0;
        public bool IsValid => SampleRate > 0 && Channels > 0 && FormatGeneration > 0;

        /// <summary>
        /// Estimates the source frame currently being rendered inside Unity's latest audio block.
        /// The committed absolute counter points at the end of that block, which can otherwise make
        /// main-thread consumers lead audible output by one complete callback.
        /// </summary>
        public long EstimateRenderedSourceFrame(double dspTime)
        {
            if (State != AudioStreamPlaybackState.Playing || SampleRate <= 0 ||
                CallbackSourceFrameCount <= 0 || CallbackDspTime <= 0d)
                return AbsoluteSourceFrame;

            double elapsedFrames = Math.Max(0d, (dspTime - CallbackDspTime) * SampleRate);
            long blockOffset = (long)Math.Floor(Math.Min(CallbackSourceFrameCount, elapsedFrames));
            long estimated = CallbackSourceFrameStart + blockOffset;
            long blockEnd = CallbackSourceFrameStart + CallbackSourceFrameCount;
            return Math.Max(CallbackSourceFrameStart, Math.Min(Math.Min(blockEnd, AbsoluteSourceFrame), estimated));
        }
    }

    /// <summary>
    /// An audio stream from a remote participant, attached to an <see cref="AudioSource"/>
    /// in the scene.
    /// </summary>
    public sealed class AudioStream : IDisposable
    {
        private const int SignalStartThreshold = 500;
        private const int SignalStopThreshold = 250;
        private const double StopTimeoutSeconds = 0.5;

        // FFI native stream is created lazily on the first OnAudioRead so we can pass
        // Unity's actual delivered (channels, sampleRate) — not a system-speaker-mode
        // guess. _handle is null until CreateOrRecreateFfiStream completes on the main
        // thread. The same path runs again whenever Unity's delivered format changes
        // mid-stream (e.g. after a system audio device switch).
        private FfiHandle _handle;
        internal FfiHandle Handle => _handle;
        private readonly ulong _trackHandleId;
        private uint _ffiNumChannels;
        private uint _ffiSampleRate;
        private bool _pendingFfiRequest;

        private readonly AudioSource _audioSource;
        private AudioProbe _probe;
        private RingBuffer _buffer;
        private short[] _tempBuffer;
        private short[] _crossfadeScratch;
        private short[] _lastOutputFrame;
        private bool _hasLastOutputFrame;
        private uint _numChannels;
        private uint _sampleRate;
        private readonly object _lock = new object();
        private readonly SynchronizationContext _mainContext;
        private bool _disposed = false;
        private bool _isStreamingActive;
        private double _lastSignalDspTime;

        // Pre-buffering state to prevent audio underruns
        private bool _isPrimed = false;
        private const float BufferSizeSeconds = 2.0f;  // Hard capacity; normal latency remains near TargetBufferSeconds.
        private const float PrimingThresholdSeconds = 0.03f;  // Wait for 30ms of data before playing
        private const float TargetBufferSeconds = 0.2f;

        // Drift correction: skip samples when buffered audio exceeds the 200ms latency target.
        // Cooldown prevents back-to-back skips, which sound like a gravelly click train;
        // one occasional skip is inaudible thanks to the crossfade in OnAudioRead.
        private const float SkipPerCallbackPercent = 0.05f;
        private const int SkipCooldownCallbacks = 10;
        private const int CrossfadeFrames = 128;  // ~2.7ms @ 48kHz
        private int _skipCooldown = 0;

        public event Action PlaybackStarted;
        public event Action PlaybackStopped;

        // Source-playhead accounting: every byte consumed from the ring buffer (read, drift-skip,
        // crossfade refill, or discarded by a clear) advances the position in the remote source
        // timeline. Consumers use this to align visuals (lip sync) to what has actually been
        // rendered to the audio device rather than to wall clock. Counters use Interlocked so the
        // main-thread getter never touches _lock (which the audio callback holds for its whole
        // body — contending on it could delay the real-time audio thread).
        private long _consumedSourceBytes;
        private long _absoluteSourceFrames;
        private long _pendingSkippedSourceFrames;
        private long _bufferedSourceFrames;
        private long _receivedSourceFrames;
        private long _renderedSourceFrames;
        private long _skippedSourceFrames;
        private long _overflowCount;
        private long _underrunCount;
        private int _formatGeneration;
        private int _playbackState = (int)AudioStreamPlaybackState.Idle;
        private int _rampFromDiscontinuity;
        private long _signalStartAbsoluteSourceFrame = -1;
        private int _signalGeneration;
        private long _callbackSourceFrameStart;
        private int _callbackSourceFrameCount;
        private double _callbackDspTime;
        private int _callbackSnapshotSequence;
        private int _discontinuityGeneration;
        private int _pendingGainCommand;
        private float _pendingTargetGain = 1f;
        private float _pendingGainDurationSeconds;
        private int _pendingSuppressAtTarget;
        private float _playbackGain = 1f;
        private float _gainEnvelopeStart = 1f;
        private float _gainEnvelopeTarget = 1f;
        private int _gainEnvelopeTotalFrames;
        private int _gainEnvelopeProcessedFrames;
        private bool _gainEnvelopeActive;
        private bool _suppressAtEnvelopeEnd;
        private int _suppressIncomingAudio;

        /// <summary>
        ///     Smoothly lowers playback without discarding the current stream.
        /// </summary>
        public void DuckPlayback(float targetGain, float durationSeconds) =>
            QueuePlaybackGain(targetGain, durationSeconds, suppressAtTarget: false);

        /// <summary>
        ///     Smoothly fades playback to silence and discards audio that belongs to the
        ///     interrupted response until <see cref="RestorePlayback" /> is called.
        /// </summary>
        public void CommitPlaybackInterruption(float durationSeconds) =>
            QueuePlaybackGain(0f, durationSeconds, suppressAtTarget: true);

        /// <summary>
        ///     Allows fresh remote audio to buffer and smoothly restores playback.
        /// </summary>
        public void RestorePlayback(float durationSeconds)
        {
            Volatile.Write(ref _suppressIncomingAudio, 0);
            QueuePlaybackGain(1f, durationSeconds, suppressAtTarget: false);
        }

        /// <summary>Absolute source-sample clock and health counters for read-only consumers.</summary>
        public AudioStreamPlaybackSnapshot PlaybackSnapshot
        {
            get
            {
                long callbackSourceFrameStart = 0;
                int callbackSourceFrameCount = 0;
                double callbackDspTime = 0d;
                int sequenceBefore;
                int sequenceAfter = 0;
                do
                {
                    sequenceBefore = Volatile.Read(ref _callbackSnapshotSequence);
                    if ((sequenceBefore & 1) != 0) continue;

                    callbackSourceFrameStart = Interlocked.Read(ref _callbackSourceFrameStart);
                    callbackSourceFrameCount = Volatile.Read(ref _callbackSourceFrameCount);
                    callbackDspTime = Volatile.Read(ref _callbackDspTime);
                    sequenceAfter = Volatile.Read(ref _callbackSnapshotSequence);
                } while (sequenceBefore != sequenceAfter || (sequenceAfter & 1) != 0);

                return new AudioStreamPlaybackSnapshot(
                    Interlocked.Read(ref _absoluteSourceFrames),
                    (int)Volatile.Read(ref _sampleRate),
                    (int)Volatile.Read(ref _numChannels),
                    Volatile.Read(ref _formatGeneration),
                    Interlocked.Read(ref _bufferedSourceFrames),
                    (AudioStreamPlaybackState)Volatile.Read(ref _playbackState),
                    Interlocked.Read(ref _receivedSourceFrames),
                    Interlocked.Read(ref _renderedSourceFrames),
                    Interlocked.Read(ref _skippedSourceFrames),
                    Interlocked.Read(ref _overflowCount),
                    Interlocked.Read(ref _underrunCount),
                    Interlocked.Read(ref _signalStartAbsoluteSourceFrame),
                    Volatile.Read(ref _signalGeneration),
                    callbackSourceFrameStart,
                    callbackSourceFrameCount,
                    callbackDspTime,
                    Volatile.Read(ref _discontinuityGeneration));
            }
        }

        /// <summary>
        /// Seconds of remote source audio consumed by the device since the current playback
        /// signal started (the same instant <see cref="PlaybackStarted"/> refers to). Freezes
        /// during underruns and after <see cref="PlaybackStopped"/>; resets when a new playback
        /// signal starts. Returns 0 before the first signal.
        /// </summary>
        public double PlayedSinceSignalStartSeconds
        {
            get
            {
                AudioStreamPlaybackSnapshot snapshot = PlaybackSnapshot;
                if (!snapshot.HasSignalStart || snapshot.SampleRate <= 0) return 0d;

                long renderedFrame = snapshot.EstimateRenderedSourceFrame(AudioSettings.dspTime);
                long frames = renderedFrame - snapshot.SignalStartAbsoluteSourceFrame;
                return frames <= 0 ? 0d : frames / (double)snapshot.SampleRate;
            }
        }

        /// <summary>
        /// Creates a new audio stream from a remote audio track, attaching it to the
        /// given <see cref="AudioSource"/> in the scene.
        /// </summary>
        /// <param name="audioTrack">The remote audio track to stream.</param>
        /// <param name="source">The audio source to play the stream on.</param>
        /// <exception cref="InvalidOperationException">Thrown if the audio track's room or
        /// participant is invalid.</exception>
        public AudioStream(RemoteAudioTrack audioTrack, AudioSource source)
        {
            if (!audioTrack.Room.TryGetTarget(out var room))
                throw new InvalidOperationException("audiotrack's room is invalid");

            if (!audioTrack.Participant.TryGetTarget(out var participant))
                throw new InvalidOperationException("audiotrack's participant is invalid");

            _trackHandleId = (ulong)(audioTrack as ITrack).TrackHandle.DangerousGetHandle();
            _mainContext = SynchronizationContext.Current;

            _audioSource = source != null ? source : throw new ArgumentNullException(nameof(source));
            _probe = _audioSource.gameObject.AddComponent<AudioProbe>();
            _probe.AudioRead += OnAudioRead;
            _audioSource.Play();

            // Subscribe to application pause events to handle background/foreground transitions
            MonoBehaviourContext.OnApplicationPauseEvent += OnApplicationPause;

            // Unity stops every AudioSource when the system audio output device changes
            // (e.g. headphones unplugged). Without re-playing the source, OnAudioFilterRead
            // stops firing and the stream goes silent until the AudioStream is recreated.
            AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;

            // FFI stream creation is deferred to the first OnAudioRead. We subscribe to
            // AudioStreamEventReceived only after the stream exists (CreateFfiStream).
        }

        // Called on the main thread (posted from OnAudioRead via FfiClient._context) when
        // either there is no FFI stream yet or Unity's delivered (channels, sampleRate) no
        // longer matches what we asked Rust for. Builds a fresh native stream and swaps it
        // in atomically. The old handle is disposed AFTER the swap so any in-flight frames
        // from the old stream fail the handle-id filter in OnAudioStreamEvent.
        private void CreateOrRecreateFfiStream(uint observedChannels, uint observedSampleRate)
        {
            lock (_lock) { if (_disposed) return; }

            FfiHandle newHandle;
            try
            {
                using var request = FFIBridge.Instance.NewRequest<NewAudioStreamRequest>();
                var req = request.request;
                req.TrackHandle = _trackHandleId;
                req.Type = AudioStreamType.AudioStreamNative;
                req.SampleRate = observedSampleRate;
                req.NumChannels = observedChannels;

                using var response = request.Send();
                FfiResponse res = response;
                newHandle = FfiHandle.FromOwnedHandle(res.NewAudioStream.Stream.Handle);
            }
            catch (Exception ex)
            {
                Utils.Error($"AudioStream FFI (re)create failed: {ex}");
                lock (_lock) { _pendingFfiRequest = false; }
                return;
            }

            FfiHandle oldHandle;
            bool firstCreate;
            lock (_lock)
            {
                if (_disposed) { newHandle.Dispose(); return; }
                oldHandle = _handle;
                firstCreate = oldHandle == null;
                _handle = newHandle;
                _ffiNumChannels = observedChannels;
                _ffiSampleRate = observedSampleRate;
                ClearBufferCountingConsumed();
                _isPrimed = false;
                _skipCooldown = 0;
                _pendingFfiRequest = false;

                if (firstCreate)
                    FfiClient.Instance.AudioStreamEventReceived += OnAudioStreamEvent;
            }
            oldHandle?.Dispose();
        }

        // Called on Unity audio thread
        private void OnAudioRead(float[] data, int channels, int sampleRate)
        {
            if (_disposed)
            {
                Array.Clear(data, 0, data.Length);
                return;
            }

            lock (_lock)
            {
                // Single gate covering first-create and runtime format changes (e.g. after a
                // system audio device switch). When the FFI stream is missing or what we asked
                // Rust for no longer matches what Unity is delivering, post a (re)create to the
                // main thread and output silence until it lands. The priming window absorbs this.
                if (_handle == null || channels != _ffiNumChannels || sampleRate != _ffiSampleRate)
                {
                    if (!_pendingFfiRequest)
                    {
                        _pendingFfiRequest = true;
                        uint observedCh = (uint)channels;
                        uint observedSr = (uint)sampleRate;
                        FfiClient.Instance._context?.Post(_ => CreateOrRecreateFfiStream(observedCh, observedSr), null);
                    }
                    UpdatePlaybackState(0);
                    Array.Clear(data, 0, data.Length);
                    return;
                }

                // Initialize or reinitialize buffer if audio format changed
                if (_buffer == null || channels != _numChannels || sampleRate != _sampleRate || data.Length != _tempBuffer.Length)
                {
                    // Two-second hard capacity absorbs scheduling stalls. Playback still primes at
                    // 30ms and drift correction targets 200ms, so capacity does not add latency.
                    int bufferSize = (int)(channels * sampleRate * BufferSizeSeconds);
                    _buffer?.Dispose();
                    _buffer = new RingBuffer(bufferSize * sizeof(short));
                    _tempBuffer = new short[data.Length];
                    _crossfadeScratch = new short[CrossfadeFrames * channels];
                    _lastOutputFrame = new short[channels];
                    _hasLastOutputFrame = false;
                    _numChannels = (uint)channels;
                    _sampleRate = (uint)sampleRate;
                    Interlocked.Exchange(ref _absoluteSourceFrames, 0);
                    Interlocked.Exchange(ref _pendingSkippedSourceFrames, 0);
                    Interlocked.Exchange(ref _bufferedSourceFrames, 0);
                    Interlocked.Exchange(ref _signalStartAbsoluteSourceFrame, -1);
                    Interlocked.Exchange(ref _callbackSourceFrameStart, 0);
                    Volatile.Write(ref _callbackSourceFrameCount, 0);
                    Volatile.Write(ref _callbackDspTime, 0d);
                    Interlocked.Increment(ref _formatGeneration);
                    Volatile.Write(ref _playbackState, (int)AudioStreamPlaybackState.Idle);

                    // Buffer was recreated, need to re-prime
                    _isPrimed = false;
                    _skipCooldown = 0;
                }

                ApplyPendingGainCommand(sampleRate);
                if (Volatile.Read(ref _suppressIncomingAudio) != 0)
                {
                    StopPlaybackImmediately();
                    Array.Clear(data, 0, data.Length);
                    return;
                }

                static float S16ToFloat(short v)
                {
                    return v / 32768f;
                }

                // Check if we have enough data in the buffer to start/continue playback.
                // Pre-buffering strategy: wait for 30ms of data before playing to avoid underruns.
                int primingThresholdBytes = (int)(channels * sampleRate * PrimingThresholdSeconds * sizeof(short));
                int availableBytes = _buffer.AvailableRead();

                if (!_isPrimed)
                {
                    // Not yet primed - check if we have enough data to start playing
                    if (availableBytes >= primingThresholdBytes)
                    {
                        _isPrimed = true;
                    }
                    else
                    {
                        // Not enough data yet, output silence and wait
                        UpdatePlaybackState(0);
                        FillSilenceWithRamp(data, channels);
                        return;
                    }
                }

                int valuesAvailableToRead = _buffer.AvailableRead() / sizeof(short);
                // Underrun detection: If we couldn't read enough samples, immediately output silence
                // and wait for the buffer to refill enough to offer Unity a full sample.
                // This prevents choppy audio from playing partial samples during underrun.
                if (valuesAvailableToRead < data.Length)
                {
                    _isPrimed = false;
                    Interlocked.Increment(ref _underrunCount);
                    Volatile.Write(ref _playbackState, (int)AudioStreamPlaybackState.Underrun);

                    // Output silence immediately instead of playing partial/choppy samples.
                    // On next frames, the !_isPrimed check above will ensure we wait for 30ms
                    // of data before resuming playback smoothly.
                    UpdatePlaybackState(0);
                    FillSilenceWithRamp(data, channels);
                    return;
                }

                // Try to read audio samples from the ring buffer into our temp buffer.
                // The ring buffer acts as a jitter buffer between:
                // - Rust FFI pushing frames (on main thread, with network timing)
                // - Unity audio thread pulling samples (real-time, consistent timing)
                double callbackDspTime = AudioSettings.dspTime;
                long absoluteBeforeRead = Interlocked.Read(ref _absoluteSourceFrames);
                var temp = MemoryMarshal.Cast<short, byte>(_tempBuffer.AsSpan().Slice(0, data.Length));
                int bytesRead = _buffer.Read(temp);
                Interlocked.Add(ref _consumedSourceBytes, bytesRead);
                int sourceFramesRead = bytesRead / (channels * sizeof(short));
                long skippedBeforeRead = Interlocked.Exchange(ref _pendingSkippedSourceFrames, 0);
                long callbackSourceFrameStart = absoluteBeforeRead + skippedBeforeRead;
                Interlocked.Add(ref _absoluteSourceFrames, skippedBeforeRead + sourceFramesRead);
                if (skippedBeforeRead > 0) Volatile.Write(ref _discontinuityGeneration,
                    Volatile.Read(ref _discontinuityGeneration) + 1);
                Interlocked.Add(ref _renderedSourceFrames, sourceFramesRead);
                Interlocked.Exchange(ref _bufferedSourceFrames,
                    _buffer.AvailableRead() / (channels * sizeof(short)));
                Volatile.Write(ref _playbackState, (int)AudioStreamPlaybackState.Playing);

                // Calculate how many samples (shorts) were actually read from the ring buffer.
                // If the buffer is empty or doesn't have enough data, bytesRead will be less than requested.
                int samplesRead = bytesRead / sizeof(short);
                if (Interlocked.Exchange(ref _rampFromDiscontinuity, 0) != 0 && _hasLastOutputFrame)
                    RampFromFrame(_tempBuffer, samplesRead, channels, _lastOutputFrame, CrossfadeFrames);
                int peak = GetPeakAndFirstSignalFrame(
                    _tempBuffer,
                    samplesRead,
                    channels,
                    SignalStartThreshold,
                    out int firstSignalFrame);
                long signalStartFrame = firstSignalFrame >= 0
                    ? callbackSourceFrameStart + firstSignalFrame
                    : -1;
                UpdatePlaybackState(peak, signalStartFrame);

                // Drift correction: if the buffer is filling up (producer faster than
                // consumer), discard a small run of samples and crossfade the output tail
                // into the post-skip window so the seam is inaudible. Cooldown spaces
                // skips out so we never produce back-to-back artifacts.
                if (_skipCooldown > 0)
                    _skipCooldown--;

                int highWaterBytes = (int)(channels * sampleRate * TargetBufferSeconds * sizeof(short));
                if (_skipCooldown == 0 && _buffer.AvailableRead() > highWaterBytes)
                {
                    int frameSize = channels * sizeof(short);
                    int skipBytes = (int)(data.Length * sizeof(short) * SkipPerCallbackPercent);
                    skipBytes -= skipBytes % frameSize; // align to frame boundary

                    int crossfadeShorts = CrossfadeFrames * channels;
                    int crossfadeBytes = crossfadeShorts * sizeof(short);

                    // Only skip if we still have enough data left to fill the crossfade
                    // window; never trade a catch-up artifact for an underrun artifact.
                    if (skipBytes > 0 && _buffer.AvailableRead() >= skipBytes + crossfadeBytes)
                    {
                        _buffer.SkipRead(skipBytes);

                        var postBytes = MemoryMarshal.Cast<short, byte>(_crossfadeScratch.AsSpan().Slice(0, crossfadeShorts));
                        _buffer.Read(postBytes);
                        Interlocked.Add(ref _consumedSourceBytes, skipBytes + crossfadeBytes);
                        int skippedFrames = skipBytes / frameSize;
                        int crossfadeSourceFrames = crossfadeBytes / frameSize;
                        Interlocked.Add(ref _absoluteSourceFrames, skippedFrames + crossfadeSourceFrames);
                        Interlocked.Add(ref _skippedSourceFrames, skippedFrames);
                        Volatile.Write(ref _discontinuityGeneration,
                            Volatile.Read(ref _discontinuityGeneration) + 1);
                        Interlocked.Exchange(ref _bufferedSourceFrames,
                            _buffer.AvailableRead() / frameSize);

                        // Linearly crossfade the last crossfadeFrames frames of _tempBuffer
                        // with the post-skip samples: the step discontinuity becomes a
                        // short linear ramp that is continuous with the next callback.
                        int tailStart = samplesRead - crossfadeShorts;
                        if (tailStart >= 0)
                        {
                            for (int i = 0; i < crossfadeShorts; i++)
                            {
                                int frameIdx = i / channels;
                                float t = (frameIdx + 1) / (float)CrossfadeFrames;
                                short pre = _tempBuffer[tailStart + i];
                                short post = _crossfadeScratch[i];
                                _tempBuffer[tailStart + i] = (short)(pre * (1f - t) + post * t);
                            }
                            _skipCooldown = SkipCooldownCallbacks;
                        }
                    }
                }

                ApplyPlaybackGainEnvelope(_tempBuffer, samplesRead, channels, sampleRate);

                // Clear the entire output buffer to silence, then fill with the samples
                // we successfully read from the ring buffer.
                Array.Clear(data, 0, data.Length);
                for (int i = 0; i < samplesRead; i++)
                {
                    data[i] = S16ToFloat(_tempBuffer[i]);
                }

                int renderedFrames = samplesRead / channels;
                if (renderedFrames > 0)
                {
                    int lastFrameStart = (renderedFrames - 1) * channels;
                    for (int channel = 0; channel < channels; channel++)
                        _lastOutputFrame[channel] = _tempBuffer[lastFrameStart + channel];
                    _hasLastOutputFrame = true;
                }

                // Publish callback mapping only after the output block is completely assembled.
                // A main-thread snapshot can then interpolate within a coherent source range.
                Volatile.Write(ref _callbackSnapshotSequence,
                    Volatile.Read(ref _callbackSnapshotSequence) + 1);
                Interlocked.Exchange(ref _callbackSourceFrameStart, callbackSourceFrameStart);
                Volatile.Write(ref _callbackSourceFrameCount, sourceFramesRead);
                Volatile.Write(ref _callbackDspTime, callbackDspTime);
                Volatile.Write(ref _callbackSnapshotSequence,
                    Volatile.Read(ref _callbackSnapshotSequence) + 1);
            }
        }

        internal static void RampFromFrame(
            short[] samples,
            int sampleCount,
            int channels,
            short[] sourceFrame,
            int maxFrames)
        {
            if (samples == null || sourceFrame == null || channels <= 0 || sampleCount <= 0) return;

            int frameCount = Math.Min(maxFrames, sampleCount / channels);
            for (int frame = 0; frame < frameCount; frame++)
            {
                float t = (frame + 1f) / frameCount;
                int frameStart = frame * channels;
                for (int channel = 0; channel < channels; channel++)
                {
                    short from = channel < sourceFrame.Length ? sourceFrame[channel] : (short)0;
                    short to = samples[frameStart + channel];
                    samples[frameStart + channel] = (short)(from * (1f - t) + to * t);
                }
            }
        }

        internal static int GetPcm16Magnitude(short sample)
        {
            // Widen before negation: Math.Abs(short.MinValue) overflows because +32768
            // cannot be represented by a signed 16-bit integer.
            return Math.Abs((int)sample);
        }

        internal static int GetPeakAndFirstSignalFrame(
            short[] samples,
            int sampleCount,
            int channels,
            int threshold,
            out int firstSignalFrame)
        {
            firstSignalFrame = -1;
            if (samples == null || channels <= 0 || sampleCount <= 0) return 0;

            int peak = 0;
            int limit = Math.Min(sampleCount, samples.Length);
            int frameCount = limit / channels;
            for (int frame = 0; frame < frameCount; frame++)
            {
                int framePeak = 0;
                int frameStart = frame * channels;
                for (int channel = 0; channel < channels; channel++)
                {
                    int magnitude = GetPcm16Magnitude(samples[frameStart + channel]);
                    if (magnitude > framePeak) framePeak = magnitude;
                }

                if (framePeak > peak) peak = framePeak;
                if (firstSignalFrame < 0 && framePeak >= threshold) firstSignalFrame = frame;
            }

            return peak;
        }

        private void FillSilenceWithRamp(float[] output, int channels)
        {
            Array.Clear(output, 0, output.Length);
            if (!_hasLastOutputFrame || _lastOutputFrame == null || channels <= 0) return;

            int frameCount = Math.Min(CrossfadeFrames, output.Length / channels);
            for (int frame = 0; frame < frameCount; frame++)
            {
                float scale = 1f - (frame + 1f) / frameCount;
                int frameStart = frame * channels;
                for (int channel = 0; channel < channels; channel++)
                    output[frameStart + channel] = _lastOutputFrame[channel] / 32768f * scale;
            }

            _hasLastOutputFrame = false;
        }

        private void QueuePlaybackGain(float targetGain, float durationSeconds, bool suppressAtTarget)
        {
            if (_disposed) return;

            _pendingTargetGain = Mathf.Clamp01(targetGain);
            _pendingGainDurationSeconds = Mathf.Max(0f, durationSeconds);
            Volatile.Write(ref _pendingSuppressAtTarget, suppressAtTarget ? 1 : 0);
            Interlocked.Exchange(ref _pendingGainCommand, 1);
        }

        private void ApplyPendingGainCommand(int sampleRate)
        {
            if (Interlocked.Exchange(ref _pendingGainCommand, 0) == 0)
                return;

            _gainEnvelopeStart = Mathf.Clamp01(_playbackGain);
            _gainEnvelopeTarget = Mathf.Clamp01(_pendingTargetGain);
            _gainEnvelopeTotalFrames = Math.Max(
                1,
                (int)Math.Round(Math.Max(0f, _pendingGainDurationSeconds) * Math.Max(1, sampleRate)));
            _gainEnvelopeProcessedFrames = 0;
            _gainEnvelopeActive = true;
            _suppressAtEnvelopeEnd = Volatile.Read(ref _pendingSuppressAtTarget) != 0;

            if (_gainEnvelopeTarget > 0f)
                Volatile.Write(ref _suppressIncomingAudio, 0);
        }

        private void ApplyPlaybackGainEnvelope(short[] samples, int sampleCount, int channels, int sampleRate)
        {
            if (samples == null || sampleCount <= 0 || channels <= 0)
                return;

            int frameCount = Math.Min(sampleCount, samples.Length) / channels;
            for (int frame = 0; frame < frameCount; frame++)
            {
                if (_gainEnvelopeActive)
                {
                    _gainEnvelopeProcessedFrames++;
                    _playbackGain = EvaluatePlaybackGain(
                        _gainEnvelopeStart,
                        _gainEnvelopeTarget,
                        _gainEnvelopeProcessedFrames,
                        _gainEnvelopeTotalFrames);
                }

                int frameStart = frame * channels;
                for (int channel = 0; channel < channels; channel++)
                    samples[frameStart + channel] = (short)(samples[frameStart + channel] * _playbackGain);

                if (!_gainEnvelopeActive ||
                    _gainEnvelopeProcessedFrames < _gainEnvelopeTotalFrames)
                    continue;

                _gainEnvelopeActive = false;
                _playbackGain = _gainEnvelopeTarget;
                if (_suppressAtEnvelopeEnd && _gainEnvelopeTarget <= 0f)
                {
                    Volatile.Write(ref _suppressIncomingAudio, 1);
                    ClearBufferCountingConsumed();
                    _isPrimed = false;
                    StopPlaybackImmediately();
                }
            }
        }

        internal static float EvaluatePlaybackGain(
            float startGain,
            float targetGain,
            int processedFrames,
            int totalFrames)
        {
            if (totalFrames <= 0)
                return Mathf.Clamp01(targetGain);

            float progress = Mathf.Clamp01(processedFrames / (float)totalFrames);
            float easedRemainder = 1f - progress;
            easedRemainder *= easedRemainder;
            return Mathf.Clamp01(targetGain + (startGain - targetGain) * easedRemainder);
        }

        private void StopPlaybackImmediately()
        {
            Volatile.Write(ref _playbackState, (int)AudioStreamPlaybackState.Idle);
            if (!_isStreamingActive) return;

            _isStreamingActive = false;
            Post(() => PlaybackStopped?.Invoke());
        }

        // Counts still-buffered bytes as consumed before discarding them: dropped audio advances
        // the position in the source timeline even though it is never rendered. Callers must
        // hold _lock.
        private void ClearBufferCountingConsumed()
        {
            if (_buffer == null) return;
            int frameSize = Math.Max(sizeof(short), (int)_numChannels * sizeof(short));
            int bufferedBytes = _buffer.AvailableRead();
            long skippedFrames = bufferedBytes / frameSize;
            Interlocked.Add(ref _consumedSourceBytes, bufferedBytes);
            Interlocked.Add(ref _pendingSkippedSourceFrames, skippedFrames);
            Interlocked.Add(ref _skippedSourceFrames, skippedFrames);
            _buffer.Clear();
            Interlocked.Exchange(ref _bufferedSourceFrames, 0);
        }

        private void UpdatePlaybackState(int peak, long signalStartAbsoluteSourceFrame = -1)
        {
            double dspNow = AudioSettings.dspTime;
            if (peak >= SignalStartThreshold)
            {
                _lastSignalDspTime = dspNow;
                if (!_isStreamingActive)
                {
                    _isStreamingActive = true;
                    long exactStart = signalStartAbsoluteSourceFrame >= 0
                        ? signalStartAbsoluteSourceFrame
                        : Interlocked.Read(ref _absoluteSourceFrames);
                    Interlocked.Exchange(ref _signalStartAbsoluteSourceFrame, exactStart);
                    Volatile.Write(ref _signalGeneration, Volatile.Read(ref _signalGeneration) + 1);
                    Post(() => PlaybackStarted?.Invoke());
                }
            }
            else if (peak < SignalStopThreshold && _isStreamingActive)
            {
                if ((dspNow - _lastSignalDspTime) > StopTimeoutSeconds)
                {
                    _isStreamingActive = false;
                    Post(() => PlaybackStopped?.Invoke());
                }
            }
        }

        private void Post(Action action)
        {
            if (action == null)
                return;

            if (_mainContext != null)
                _mainContext.Post(_ => action(), null);
            else
                action();
        }

        // Called when the system audio output device changes (e.g. plug/unplug headphones)
        // or AudioSettings.Reset is invoked. Unity tears down its audio engine in both cases,
        // which stops every AudioSource and detaches the AudioProbe filter from the rebuilt
        // audio graph. Just calling Play() on the existing source isn't always enough; we
        // additionally recreate the AudioProbe so Unity re-registers the OnAudioFilterRead
        // node on the new graph.
        private void OnAudioConfigurationChanged(bool deviceWasChanged)
        {
            if (_disposed) return;

            if (_audioSource == null)
            {
                Dispose();
                return;
            }

            lock (_lock)
            {
                ClearBufferCountingConsumed();
                _isPrimed = false;
            }

            DestroyProbe();

            _probe = _audioSource.gameObject.AddComponent<AudioProbe>();
            _probe.AudioRead += OnAudioRead;

            _audioSource.Stop();
            _audioSource.Play();
        }

        // Called when application goes to background or returns to foreground
        internal void OnApplicationPause(bool pause)
        {
            if (_disposed)
                return;

            // When returning from background, clear the ring buffer and reset priming state.
            // This ensures we don't play stale audio data and forces the stream to wait
            // for fresh data (30ms) before resuming playback, preventing audio glitches.
            // Also set a timestamp to discard any stale FFI callbacks that arrive shortly after.
            if (!pause)  // Returning to foreground
            {
                lock (_lock)
                {
                    if (_buffer != null)
                    {
                        ClearBufferCountingConsumed();
                        _isPrimed = false;
                        UpdatePlaybackState(0);
                        Utils.Debug("AudioStream cleared buffer on app resume, waiting to re-prime");
                    }
                }
            }
        }

        // Called on FFI callback thread
        private void OnAudioStreamEvent(AudioStreamEvent e)
        {
            if (_disposed)
                return;

            if ((ulong)Handle.DangerousGetHandle() != e.StreamHandle)
                return;

            if (e.MessageCase != AudioStreamEvent.MessageOneofCase.FrameReceived)
                return;

            using var frame = new AudioFrame(e.FrameReceived.Frame);

            lock (_lock)
            {
                // _pendingFfiRequest gates writes during a (re)create: between the moment
                // OnAudioRead detects a format mismatch and the swap landing, Rust is still
                // emitting frames at the OLD format. Drop them to avoid corrupting the buffer.
                // The handle-id filter above is the second line of defense for stragglers
                // arriving from the old stream after the swap.
                if (_buffer == null || _pendingFfiRequest ||
                    Volatile.Read(ref _suppressIncomingAudio) != 0)
                    return;

                unsafe
                {
                    var data = new ReadOnlySpan<byte>(frame.Data.ToPointer(), frame.Length);
                    WriteSourceFrame(data);
                }
            }
        }

        /// <summary>
        /// Preserves newest real-time audio under pressure. Oldest complete source frames are
        /// discarded before an all-or-nothing write; the source clock applies that jump only when
        /// the next callback actually renders post-skip audio.
        /// </summary>
        private void WriteSourceFrame(ReadOnlySpan<byte> data)
        {
            int channels = Math.Max(1, (int)_numChannels);
            int frameSize = channels * sizeof(short);
            int originalBytes = data.Length - data.Length % frameSize;
            if (originalBytes <= 0) return;

            data = data.Slice(0, originalBytes);
            Interlocked.Add(ref _receivedSourceFrames, originalBytes / frameSize);

            int usableCapacity = _buffer.Capacity - _buffer.Capacity % frameSize;
            if (data.Length > usableCapacity)
            {
                int bufferedBytes = _buffer.AvailableRead();
                int leadingBytes = data.Length - usableCapacity;
                _buffer.Clear();
                data = data.Slice(leadingBytes);
                RegisterOverflowSkip(bufferedBytes + leadingBytes, frameSize);
            }
            else
            {
                int bytesNeeded = data.Length - _buffer.AvailableWrite();
                if (bytesNeeded > 0)
                {
                    int alignedBytes = ((bytesNeeded + frameSize - 1) / frameSize) * frameSize;
                    int skippedBytes = _buffer.SkipRead(alignedBytes);
                    RegisterOverflowSkip(skippedBytes, frameSize);
                }
            }

            if (!_buffer.WriteExact(data))
            {
                RegisterOverflowSkip(data.Length, frameSize);
                return;
            }

            Interlocked.Exchange(ref _bufferedSourceFrames, _buffer.AvailableRead() / frameSize);
        }

        private void RegisterOverflowSkip(int skippedBytes, int frameSize)
        {
            if (skippedBytes <= 0) return;

            long skippedFrames = skippedBytes / frameSize;
            Interlocked.Add(ref _pendingSkippedSourceFrames, skippedFrames);
            Interlocked.Add(ref _skippedSourceFrames, skippedFrames);
            Interlocked.Increment(ref _overflowCount);
            Interlocked.Exchange(ref _rampFromDiscontinuity, 1);
            Interlocked.Exchange(ref _bufferedSourceFrames, _buffer.AvailableRead() / frameSize);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            // Remove long-lived delegate references first so this instance can become collectible
            // as soon as user code drops it. This also prevents late native callbacks from
            // touching partially disposed state. AudioStreamEventReceived was only subscribed
            // after CreateFfiStream succeeded; -= against an unsubscribed handler is a no-op,
            // but the explicit guard documents the lifecycle.
            if (_handle != null)
                FfiClient.Instance.AudioStreamEventReceived -= OnAudioStreamEvent;
            MonoBehaviourContext.OnApplicationPauseEvent -= OnApplicationPause;
            AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;

            lock (_lock)
            {
                // Native resources can be released on both the explicit-dispose and finalizer
                // paths. Unity objects are only touched when Dispose() is called explicitly on the
                // main thread.
                if (_isStreamingActive)
                {
                    _isStreamingActive = false;
                    Post(() => PlaybackStopped?.Invoke());
                }

                if (disposing)
                {
                    if (_audioSource != null)
                        _audioSource.Stop();

                    DestroyProbe();
                }

                _buffer?.Dispose();
                _buffer = null;
                _tempBuffer = null;
                _crossfadeScratch = null;
                _lastOutputFrame = null;
                _handle?.Dispose();
                _handle = null;
            }

            _disposed = true;
            Volatile.Write(ref _playbackState, (int)AudioStreamPlaybackState.Disposed);
        }

        private void DestroyProbe()
        {
            if (_probe == null)
            {
                _probe = null;
                return;
            }

            _probe.AudioRead -= OnAudioRead;
            UnityEngine.Object.Destroy(_probe);
            _probe = null;
        }

        ~AudioStream()
        {
            Dispose(false);
        }

        // For testing and debugging
        internal float GetBufferFill()
        {
            lock(_lock)
            {
                if (_buffer == null)
                    return 0;
                return _buffer.AvailableReadInPercent();
            }

        }
    }
}
