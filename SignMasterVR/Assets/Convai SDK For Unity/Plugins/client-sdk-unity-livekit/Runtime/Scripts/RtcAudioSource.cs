using System;
using System.Collections;
using System.Collections.Generic;
using LiveKit.Proto;
using LiveKit.Internal;
using LiveKit.Internal.FFIClients.Requests;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using System.Diagnostics;
using System.Threading;
using UnityEngine;

namespace LiveKit
{
    /// <summary>
    /// Defines the type of audio source, influencing processing behavior.
    /// </summary>
    public enum RtcAudioSourceType
    {
        AudioSourceCustom = 0,
        AudioSourceMicrophone = 1
    }

    /// <summary>
    /// Capture source for a local audio track.
    /// </summary>
    public abstract class RtcAudioSource : IRtcSource, IDisposable
    {
        private const int MaxApmStreamDelayMs = 500;

        private sealed class PendingAudioFrame
        {
            public NativeArray<short> FrameData;
            public int FrameIndex;
            public int SampleRate;
            public int Channels;
            public int SampleCount;
            public long StartedTimestamp;
        }

        private static int nextDebugId = 0;

        /// <summary>
        /// Event triggered when audio samples are captured from the underlying source.
        /// Provides the audio data, channel count, and sample rate.
        /// </summary>
        /// <remarks>
        /// This event is not guaranteed to be called on the main thread.
        /// </remarks>
        public abstract event Action<float[], int, int> AudioRead;

#if UNITY_IOS && !UNITY_EDITOR
        // iOS microphone sample rate is 24k
        public static uint DefaultMicrophoneSampleRate = 24000;

        public static uint DefaultSampleRate = 48000;
#else
        public static uint DefaultSampleRate = 48000;
        public static uint DefaultMicrophoneSampleRate = DefaultSampleRate;
#endif
        public static uint DefaultChannels = 2;

        private readonly RtcAudioSourceType _sourceType;
        public RtcAudioSourceType SourceType => _sourceType;

        /// <summary>
        /// Raised synchronously with the PCM that will be published. When the explicit audio
        /// processing module is active, these samples have already passed through AEC, noise
        /// suppression, high-pass filtering, and gain control.
        /// </summary>
        /// <remarks>
        /// The sample array is reused by the source. Subscribers must copy any samples they
        /// need before returning from the callback.
        /// </remarks>
        public event Action<float[], int, int> ProcessedAudioRead;

        /// <summary>
        /// Whether software AEC is initialized and has an active rendered-audio reference stream.
        /// </summary>
        public bool IsAcousticEchoCancellationActive =>
            _apm != null && _apmReverseStream?.IsActive == true;

        private readonly int _debugId = Interlocked.Increment(ref nextDebugId);
        private readonly uint _expectedSampleRate;
        private readonly uint _expectedChannels;

        internal readonly FfiHandle Handle;
        protected AudioSourceInfo _info;
        private readonly AudioBuffer _captureBuffer = new();
        private readonly AudioProcessingModule _apm;
        private readonly ApmReverseStream _apmReverseStream;

        // CaptureAudioFrame is asynchronous: the native side can continue reading from the PCM
        // pointer after request.Send() returns and encode it later on another queue. Because of
        // that, a single reusable NativeArray is unsafe here; the next AudioRead callback can
        // overwrite it while Opus/WebRTC is still consuming the previous frame.
        //
        // Keep one NativeArray per in-flight request and release it only after the matching
        // CaptureAudioFrame callback completes or is canceled.
        private readonly Dictionary<ulong, PendingAudioFrame> _pendingFrameData = new();
        private readonly object _pendingFrameDataLock = new object();

        private volatile bool _muted = false;
        public override bool Muted => _muted;

        private bool _started = false;
        private volatile bool _disposed = false;
        private volatile bool _acceptAudioCallbacks;
        private int _audioReadCount = 0;
        private float[] _processedAudioData;

        protected RtcAudioSource(
            int channels = 2,
            RtcAudioSourceType audioSourceType = RtcAudioSourceType.AudioSourceCustom,
            bool enableAcousticEchoCancellation = false)
            : this(audioSourceType, 0, (uint)channels, enableAcousticEchoCancellation) { }

        protected RtcAudioSource(RtcAudioSourceType audioSourceType)
            : this(audioSourceType, 0, 0) { }

        protected RtcAudioSource(RtcAudioSourceType audioSourceType, uint sampleRate, uint channels)
            : this(audioSourceType, sampleRate, channels, false) { }

        protected RtcAudioSource(
            RtcAudioSourceType audioSourceType,
            uint sampleRate,
            uint channels,
            bool enableAcousticEchoCancellation)
        {
            _sourceType = audioSourceType;
            if (sampleRate > 0 && channels > 0)
            {
                _expectedSampleRate = sampleRate;
                _expectedChannels = channels;
            }
            else
            {
                (_expectedSampleRate, _expectedChannels) = ResolveDeviceFormat();
            }

            bool isMicrophone = _sourceType == RtcAudioSourceType.AudioSourceMicrophone;

            if (enableAcousticEchoCancellation && isMicrophone)
            {
                try
                {
                    _apm = new AudioProcessingModule(true, true, true, true);
                    _apm.SetStreamDelayMs(EstimateStreamDelayMs());
                    _apmReverseStream = new ApmReverseStream(_apm);
                }
                catch (Exception ex)
                {
                    Utils.Error($"Failed to initialize acoustic echo cancellation: {ex.Message}");
                }
            }

            using var request = FFIBridge.Instance.NewRequest<NewAudioSourceRequest>();
            var newAudioSource = request.request;
            newAudioSource.Type = AudioSourceType.AudioSourceNative;
            newAudioSource.NumChannels = _expectedChannels;
            newAudioSource.SampleRate = _expectedSampleRate;

            UnityEngine.Debug.Log($"NewAudioSource: {newAudioSource.NumChannels} {newAudioSource.SampleRate}");

            if (_apm == null)
            {
                newAudioSource.Options = request.TempResource<AudioSourceOptions>();
                newAudioSource.Options.EchoCancellation = true;
                newAudioSource.Options.AutoGainControl = true;
                newAudioSource.Options.NoiseSuppression = true;
            }
            using var response = request.Send();
            FfiResponse res = response;
            _info = res.NewAudioSource.Source.Info;
            Handle = FfiHandle.FromOwnedHandle(res.NewAudioSource.Source.Handle);
            Utils.Debug($"{DebugTag} created handle={Handle.DangerousGetHandle()} expectedRate={_expectedSampleRate} expectedChannels={_expectedChannels} sourceType={_sourceType}");
        }

        /// <summary>
        /// Begin capturing audio samples from the underlying source.
        /// </summary>
        public virtual void Start()
        {
            if (_started) return;
            _acceptAudioCallbacks = true;
            _apmReverseStream?.Start();
            AudioRead += OnAudioRead;
            _started = true;
            Utils.Debug($"{DebugTag} start");
        }

        /// <summary>
        /// Stop capturing audio samples from the underlying source.
        /// </summary>
        public virtual void Stop()
        {
            if (!_started) return;
            _acceptAudioCallbacks = false;
            AudioRead -= OnAudioRead;
            _apmReverseStream?.Stop();
            _started = false;
            var pendingCount = PendingFrameCount();
            if (pendingCount > 0)
                Utils.Warning($"{DebugTag} stop requested with {pendingCount} pending capture callbacks");
            else
                Utils.Debug($"{DebugTag} stop");
        }

        private void OnAudioRead(float[] data, int channels, int sampleRate)
        {
            if (!_acceptAudioCallbacks)
            {
                ClearPendingAudio(data);
                return;
            }

            if (!FfiClient.IsOperational)
            {
                HandleFfiShutdownDuringAudioCallback(data);
                return;
            }

            if (_muted)
            {
                _captureBuffer.Clear();
                ClearMicrophonePlayback(data);
                return;
            }

            if (_disposed) return;

            var frameIndex = Interlocked.Increment(ref _audioReadCount);
            if (channels <= 0)
            {
                Utils.Warning($"{DebugTag} dropping audio frame #{frameIndex} because channels={channels}");
                return;
            }

            if (data.Length == 0 || data.Length % channels != 0)
            {
                Utils.Warning($"{DebugTag} audio frame #{frameIndex} has invalid shape samples={data.Length} channels={channels}");
                return;
            }

            if ((uint)sampleRate != _expectedSampleRate || (uint)channels != _expectedChannels)
            {
                Utils.Warning($"{DebugTag} audio frame #{frameIndex} metadata mismatch actualRate={sampleRate} actualChannels={channels} expectedRate={_expectedSampleRate} expectedChannels={_expectedChannels} sourceType={_sourceType}");
            }

            var pendingBeforeSend = PendingFrameCount();
            if (frameIndex <= 3 || frameIndex % 100 == 0 || pendingBeforeSend >= 3)
            {
                Utils.Debug($"{DebugTag} capture frame #{frameIndex} samples={data.Length} channels={channels} sampleRate={sampleRate} pendingBeforeSend={pendingBeforeSend} thread={Thread.CurrentThread.ManagedThreadId}");
            }

            if (_apm != null)
            {
                CaptureApmFrames(data, channels, sampleRate, frameIndex);
                ClearMicrophonePlayback(data);
                return;
            }

            RaiseProcessedAudioRead(data, channels, sampleRate);
            var frameData = CreateFrameData(data);
            try
            {
                CaptureFrame(frameData, (uint)channels, (uint)sampleRate, (uint)data.Length / (uint)channels, frameIndex);
            }
            catch (Exception ex) when (FfiClient.ShouldIgnoreShutdownException(ex))
            {
                HandleFfiShutdownDuringAudioCallback(data);
                return;
            }
            catch (Exception ex)
            {
                Utils.Error($"Audio capture failed: {ex.Message}");
                return;
            }

            ClearMicrophonePlayback(data);
        }

        private static NativeArray<short> CreateFrameData(float[] data)
        {
            var frameData = new NativeArray<short>(data.Length, Allocator.Persistent);
            static short FloatToS16(float v)
            {
                v *= 32768f;
                v = Math.Min(v, 32767f);
                v = Math.Max(v, -32768f);
                return (short)(v + Math.Sign(v) * 0.5f);
            }
            for (int i = 0; i < data.Length; i++)
                frameData[i] = FloatToS16(data[i]);

            return frameData;
        }

        private static NativeArray<short> CreateFrameData(AudioFrame frame)
        {
            var frameData = new NativeArray<short>(frame.Length / sizeof(short), Allocator.Persistent);
            unsafe
            {
                Buffer.MemoryCopy(
                    frame.Data.ToPointer(),
                    NativeArrayUnsafeUtility.GetUnsafePtr(frameData),
                    frame.Length,
                    frame.Length);
            }

            return frameData;
        }

        private void CaptureApmFrames(float[] data, int channels, int sampleRate, int frameIndex)
        {
            _captureBuffer.Write(data, (uint)channels, (uint)sampleRate);
            while (true)
            {
                AudioFrame frame = _captureBuffer.ReadDuration(AudioProcessingModule.FrameDurationMs);
                if (frame == null)
                    break;

                try
                {
                    _apm.ProcessStream(frame);
                    RaiseProcessedAudioRead(frame);
                    var frameData = CreateFrameData(frame);
                    CaptureFrame(frameData, frame.NumChannels, frame.SampleRate, frame.SamplesPerChannel, frameIndex);
                }
                catch (Exception ex) when (FfiClient.ShouldIgnoreShutdownException(ex))
                {
                    frame.Dispose();
                    HandleFfiShutdownDuringAudioCallback(data);
                    break;
                }
                catch (Exception ex)
                {
                    frame.Dispose();
                    Utils.Error($"Audio processing failed: {ex.Message}");
                    break;
                }

                frame.Dispose();
            }
        }

        private unsafe void RaiseProcessedAudioRead(AudioFrame frame)
        {
            if (ProcessedAudioRead == null || frame == null)
                return;

            int sampleCount = checked((int)(frame.SamplesPerChannel * frame.NumChannels));
            if (_processedAudioData == null || _processedAudioData.Length != sampleCount)
                _processedAudioData = new float[sampleCount];

            var samples = (short*)frame.Data.ToPointer();
            const float shortToFloat = 1f / 32768f;
            for (int i = 0; i < sampleCount; i++)
                _processedAudioData[i] = samples[i] * shortToFloat;

            RaiseProcessedAudioRead(
                _processedAudioData,
                checked((int)frame.NumChannels),
                checked((int)frame.SampleRate));
        }

        private void RaiseProcessedAudioRead(float[] data, int channels, int sampleRate)
        {
            try
            {
                ProcessedAudioRead?.Invoke(data, channels, sampleRate);
            }
            catch (Exception ex)
            {
                // An optional local PCM observer (for example VAD) must never interrupt the
                // real-time publication path.
                Utils.Error($"Processed audio observer failed: {ex.Message}");
            }
        }

        private void CaptureFrame(
            NativeArray<short> frameData,
            uint channels,
            uint sampleRate,
            uint samplesPerChannel,
            int frameIndex)
        {
            ulong requestAsyncId = 0;
            bool pendingRegistered = false;
            using var request = FFIBridge.Instance.NewRequest<CaptureAudioFrameRequest>();
            using var audioFrameBufferInfo = request.TempResource<AudioFrameBufferInfo>();

            var pushFrame = request.request;
            pushFrame.SourceHandle = (ulong)Handle.DangerousGetHandle();
            pushFrame.Buffer = audioFrameBufferInfo;
            unsafe
            {
                 pushFrame.Buffer.DataPtr = (ulong)NativeArrayUnsafeUtility
                    .GetUnsafePtr(frameData);
            }
            pushFrame.Buffer.NumChannels = channels;
            pushFrame.Buffer.SampleRate = sampleRate;
            pushFrame.Buffer.SamplesPerChannel = samplesPerChannel;

            // Wait for async callback, log an error if the capture fails. The callback's AsyncId
            // echoes the RequestAsyncId that Unity wrote onto the request.
            requestAsyncId = request.RequestAsyncId;
            var pendingFrame = new PendingAudioFrame
            {
                FrameData = frameData,
                FrameIndex = frameIndex,
                SampleRate = (int)sampleRate,
                Channels = (int)channels,
                SampleCount = (int)(samplesPerChannel * channels),
                StartedTimestamp = Stopwatch.GetTimestamp(),
            };
            lock (_pendingFrameDataLock)
            {
                _pendingFrameData[requestAsyncId] = pendingFrame;
            }

            void Callback(CaptureAudioFrameCallback callback)
            {
                if (callback.AsyncId != requestAsyncId) return;
                var completedFrame = ReleasePendingFrameData(requestAsyncId);
                if (completedFrame != null)
                {
                    var elapsedMs = ElapsedMilliseconds(completedFrame.StartedTimestamp);
                    if (callback.HasError)
                    {
                        Utils.Error($"{DebugTag} capture callback failed asyncId={requestAsyncId} frame={completedFrame.FrameIndex} elapsedMs={elapsedMs:F1} pendingAfter={PendingFrameCount()} error={callback.Error}");
                    }
                    else if (completedFrame.FrameIndex <= 3 || completedFrame.FrameIndex % 100 == 0 || elapsedMs > 100)
                    {
                        Utils.Debug($"{DebugTag} capture callback asyncId={requestAsyncId} frame={completedFrame.FrameIndex} elapsedMs={elapsedMs:F1} pendingAfter={PendingFrameCount()}");
                    }
                }
                if (callback.HasError)
                    Utils.Error($"{DebugTag} audio capture failed: {callback.Error}");
            }
            void OnCanceled()
            {
                var canceledFrame = ReleasePendingFrameData(requestAsyncId);
                if (canceledFrame != null)
                {
                    var elapsedMs = ElapsedMilliseconds(canceledFrame.StartedTimestamp);
                    Utils.Warning($"{DebugTag} capture callback canceled asyncId={requestAsyncId} frame={canceledFrame.FrameIndex} elapsedMs={elapsedMs:F1} pendingAfter={PendingFrameCount()}");
                }
            }

            FfiClient.Instance.RegisterPendingCallback(requestAsyncId, static e => e.CaptureAudioFrame, Callback, OnCanceled);
            pendingRegistered = true;
            try
            {
                using var response = request.Send();
            }
            catch
            {
                if (pendingRegistered)
                {
                    var failedFrame = ReleasePendingFrameData(requestAsyncId);
                    if (failedFrame != null)
                    {
                        Utils.Error($"{DebugTag} request send failed asyncId={requestAsyncId} frame={failedFrame.FrameIndex} pendingAfter={PendingFrameCount()}");
                    }
                }
                else if (frameData.IsCreated)
                {
                    frameData.Dispose();
                }

                throw;
            }
        }

        /// <summary>
        /// Mutes or unmutes the audio source.
        /// </summary>
        public override void SetMute(bool muted)
        {
            _muted = muted;
        }

        /// <summary>
        /// Disposes of the audio source, stopping it first if necessary.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            _acceptAudioCallbacks = false;
            if (disposing) Stop();
            _captureBuffer.Dispose();
            _apmReverseStream?.Dispose();
            ProcessedAudioRead = null;
            _processedAudioData = null;

            var pendingCount = PendingFrameCount();
            if (pendingCount > 0)
                Utils.Warning($"{DebugTag} dispose(disposing={disposing}) with {pendingCount} pending capture callbacks");

            lock (_pendingFrameDataLock)
            {
                foreach (var pendingFrame in _pendingFrameData.Values)
                {
                    if (pendingFrame.FrameData.IsCreated)
                        pendingFrame.FrameData.Dispose();
                }
                _pendingFrameData.Clear();
            }
            Handle?.Dispose();
            _disposed = true;
            Utils.Debug($"{DebugTag} disposed");
        }

        private PendingAudioFrame ReleasePendingFrameData(ulong requestAsyncId)
        {
            PendingAudioFrame pendingFrame = null;
            lock (_pendingFrameDataLock)
            {
                if (_pendingFrameData.TryGetValue(requestAsyncId, out pendingFrame))
                    _pendingFrameData.Remove(requestAsyncId);
            }

            if (pendingFrame != null && pendingFrame.FrameData.IsCreated)
                pendingFrame.FrameData.Dispose();

            return pendingFrame;
        }

        private int PendingFrameCount()
        {
            lock (_pendingFrameDataLock)
            {
                return _pendingFrameData.Count;
            }
        }

        ~RtcAudioSource()
        {
            Dispose(false);
        }

        [Obsolete("No longer used, audio sources should perform any preparation in Start() asynchronously")]
        public virtual IEnumerator Prepare(float timeout = 0) { yield break; }

        [Obsolete("Use Start() instead")]
        public IEnumerator PrepareAndStart()
        {
            Start();
            yield break;
        }

        private static double ElapsedMilliseconds(long startedTimestamp)
        {
            return (Stopwatch.GetTimestamp() - startedTimestamp) * 1000.0 / Stopwatch.Frequency;
        }

        private static int EstimateStreamDelayMs()
        {
            AudioSettings.GetDSPBufferSize(out int bufferLength, out int numBuffers);
            int sampleRate = AudioSettings.outputSampleRate;
            return EstimateStreamDelayMs(bufferLength, numBuffers, sampleRate);
        }

        internal static int EstimateStreamDelayMs(int bufferLength, int numBuffers, int sampleRate)
        {
            if (sampleRate <= 0)
                return 0;

            int estimatedDelayMs = 2 * (int)(1000f * bufferLength * numBuffers / sampleRate);
            return Math.Min(MaxApmStreamDelayMs, Math.Max(0, estimatedDelayMs));
        }

        internal static int ResolveUnityCaptureSampleRate()
        {
            int outputSampleRate = AudioSettings.outputSampleRate;
            return outputSampleRate > 0 ? outputSampleRate : (int)DefaultSampleRate;
        }

        private static (uint sampleRate, uint channels) ResolveDeviceFormat()
        {
            var config = AudioSettings.GetConfiguration();
            uint sampleRate = config.sampleRate > 0 ? (uint)config.sampleRate : DefaultSampleRate;
            uint channels = SpeakerModeChannels(config.speakerMode);
            if (channels == 0)
                channels = DefaultChannels;

            Utils.Info($"Configured native audio source with sampleRate {sampleRate} and channels {channels}");
            return (sampleRate, channels);
        }

        private static uint SpeakerModeChannels(AudioSpeakerMode mode)
        {
            switch (mode)
            {
                case AudioSpeakerMode.Mono: return 1;
                case AudioSpeakerMode.Stereo: return 2;
                case AudioSpeakerMode.Quad: return 4;
                case AudioSpeakerMode.Surround: return 5;
                case AudioSpeakerMode.Mode5point1: return 6;
                case AudioSpeakerMode.Mode7point1: return 8;
                case AudioSpeakerMode.Prologic: return 2;
                default: return 0;
            }
        }

        private void HandleFfiShutdownDuringAudioCallback(float[] data)
        {
            _acceptAudioCallbacks = false;
            ClearPendingAudio(data);
        }

        private void ClearPendingAudio(float[] data)
        {
            _captureBuffer.Clear();
            ClearMicrophonePlayback(data);
        }

        private void ClearMicrophonePlayback(float[] data)
        {
            if (_sourceType != RtcAudioSourceType.AudioSourceMicrophone)
                return;

            Array.Clear(data, 0, data.Length);
        }

        private string DebugTag => $"RtcAudioSource#{_debugId}";
    }
}
