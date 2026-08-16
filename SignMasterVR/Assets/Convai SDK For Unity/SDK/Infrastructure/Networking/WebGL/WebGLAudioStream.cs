using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using AOT;
using Convai.Domain.Logging;
using Convai.Runtime.Logging;
using LiveKit;
using UnityEngine;

namespace Convai.Infrastructure.Networking.WebGL
{
    /// <summary>
    ///     WebGL implementation of <see cref="IAudioStream" /> for browser-based audio playback.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         On WebGL, audio is played through browser HTML audio elements rather than Unity's audio system.
    ///         This implementation provides the interface but actual audio is handled by the browser.
    ///     </para>
    /// </remarks>
    internal sealed class WebGLAudioStream : IAudioStream, IAudioPlaybackStateSource,
        IAudioMediaTimelineSnapshotSource, IBargeInPlaybackControl
    {
        internal static Func<HTMLAudioElement, bool> ElementPlayingEvaluator = IsElementPlaying;
        internal static Func<HTMLAudioElement, int> TimingProbeCreator = CreateTimingProbe;
        internal static Action<int, HTMLAudioElement> TimingProbeElementUpdater = UpdateTimingProbeElement;
        internal static Func<int, AudioMediaTimelineSnapshot?> TimingProbeReader = ReadTimingProbe;
        internal static Action<int> TimingProbeDisposer = DisposeTimingProbe;
        internal static Action<int, float, float> TimingProbeGainSetter = SetTimingProbeGain;
        internal static Action TimingContextResumer = ResumeTimingContextInternal;
        private static readonly Dictionary<IntPtr, WeakReference<WebGLAudioStream>> s_streamsByElement = new();
        private static readonly object s_streamsByElementLock = new();

        #region Constructor

        /// <summary>
        ///     Creates a new WebGL audio stream.
        /// </summary>
        /// <param name="track">The remote track to stream audio from.</param>
        public WebGLAudioStream(RemoteTrack track)
        {
            _track = track ?? throw new ArgumentNullException(nameof(track));
        }

        #endregion

        #region IDisposable

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;

            TeardownPlaybackTracking();
            Detach();
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        #endregion

        private sealed class ElementRegistration
        {
            public ElementRegistration(HTMLAudioElement element, JSRef playingListener, JSRef pauseListener,
                JSRef endedListener)
            {
                Element = element;
                PlayingListener = playingListener;
                PauseListener = pauseListener;
                EndedListener = endedListener;
            }

            public HTMLAudioElement Element { get; }
            public JSRef PlayingListener { get; }
            public JSRef PauseListener { get; }
            public JSRef EndedListener { get; }
        }

        #region IAudioStream Events

        /// <inheritdoc />
        /// <remarks>
        ///     On WebGL, raw audio data is not accessible from JavaScript audio elements.
        ///     This event will not fire. Use the native platform for audio data access.
        /// </remarks>
#pragma warning disable CS0067 // Event is never used - required by interface but WebGL doesn't provide raw audio data
        public event Action<float[], int, int> AudioDataReceived;
#pragma warning restore CS0067

        public event Action PlaybackStarted
        {
            add
            {
                bool wasTrackingInitialized = _playbackTrackingInitialized;
                _playbackStarted += value;
                EnsurePlaybackTrackingInitialized();

                if (ShouldInvokePlaybackStartedImmediately(wasTrackingInitialized, _playingElementHandles.Count))
                    value?.Invoke();
            }
            remove => _playbackStarted -= value;
        }

        public event Action PlaybackStopped
        {
            add
            {
                _playbackStopped += value;
                EnsurePlaybackTrackingInitialized();
            }
            remove => _playbackStopped -= value;
        }

        #endregion

        #region Private Fields

        private readonly RemoteTrack _track;
        private readonly HashSet<IntPtr> _playingElementHandles = new();
        private readonly Dictionary<IntPtr, ElementRegistration> _registeredElements = new();
        private bool _isActive;
        private bool _disposed;
        private bool _playbackTrackingInitialized;
        private int _timingProbeId;
        private IntPtr _timingElementHandle;
        private int _timingSnapshotFrame;
        private bool _timingSnapshotCached;
        private bool _timingSnapshotValid;
        private AudioMediaTimelineSnapshot _timingSnapshot;
        private Action _playbackStarted;
        private Action _playbackStopped;

        // Default audio parameters (browser handles actual values)
        private const int DefaultSampleRate = 48000;
        private const int DefaultChannels = 2;

        #endregion

        #region IAudioStream Properties

        /// <inheritdoc />
        public bool IsActive => _isActive && !_disposed;

        /// <inheritdoc />
        /// <remarks>
        ///     On WebGL, the actual sample rate is determined by the browser's audio context.
        ///     This returns a default value.
        /// </remarks>
        public int SampleRate => DefaultSampleRate;

        /// <inheritdoc />
        /// <remarks>
        ///     On WebGL, the actual channel count is determined by the browser.
        ///     This returns a default stereo value.
        /// </remarks>
        public int Channels => DefaultChannels;

        bool IAudioMediaTimelineSnapshotSource.TryGetAudioMediaTimelineSnapshot(
            out AudioMediaTimelineSnapshot snapshot)
        {
            snapshot = default;
            if (_disposed || _timingProbeId == 0) return false;

            int currentFrame = Time.frameCount;
            // The first WebGL timing read creates/resumes a cold analyser graph. Do not let a
            // signal-less snapshot hide analyser readiness or first speech onset for the rest of
            // that Unity frame. Once the first signal is known, normal per-frame caching resumes.
            if (_timingSnapshotCached &&
                _timingSnapshotFrame == currentFrame &&
                _timingSnapshot.HasSignalStart)
            {
                snapshot = _timingSnapshot;
                return _timingSnapshotValid;
            }

            AudioMediaTimelineSnapshot? value = TimingProbeReader?.Invoke(_timingProbeId);
            _timingSnapshotFrame = currentFrame;
            _timingSnapshotCached = true;
            _timingSnapshotValid = value.HasValue && value.Value.IsValid;
            _timingSnapshot = _timingSnapshotValid ? value.Value : default;
            if (!_timingSnapshotValid) return false;

            snapshot = _timingSnapshot;
            return true;
        }

        void IBargeInPlaybackControl.Duck(float targetGain, float durationSeconds) =>
            ApplyPlaybackGain(targetGain, durationSeconds);

        void IBargeInPlaybackControl.CommitInterruption(float durationSeconds) =>
            ApplyPlaybackGain(0f, durationSeconds);

        bool IBargeInPlaybackControl.Restore(float durationSeconds)
        {
            ApplyPlaybackGain(1f, durationSeconds);

            // Browser media elements remain active while their gain is at zero, so
            // there may be no new HTML "playing" event when the gain is restored.
            return !_disposed && _playingElementHandles.Count > 0;
        }

        #endregion

        #region IAudioStream Methods

        /// <inheritdoc />
        /// <remarks>
        ///     On WebGL, audio cannot be attached to a Unity AudioSource. Instead, audio plays
        ///     through browser HTML audio elements. This method will activate browser audio playback.
        /// </remarks>
        public void AttachToAudioSource(AudioSource target)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(WebGLAudioStream));

            if (_isActive)
            {
                ConvaiLogger.Warning("Stream is already active.", LogCategory.Audio);
                return;
            }

            // Attach to browser audio element
            HTMLMediaElement attachedElement = _track.Attach();
            _isActive = true;
            EnsurePlaybackTrackingInitialized();
            RegisterAttachedElement(attachedElement);
        }

        /// <inheritdoc />
        public void Detach()
        {
            if (!_isActive) return;

            _track.Detach();
            ClearRegisteredElements();
            _isActive = false;
        }

        #endregion

        #region Playback Tracking

        [MonoPInvokeCallback(typeof(JSNative.JSDelegate))]
        private static void OnHtmlAudioPlaying(IntPtr iptr)
        {
            using (var handle = new JSHandle(iptr, true))
            {
                if (!TryGetStream(handle.DangerousGetHandle(), out WebGLAudioStream stream))
                    return;

                stream.MarkElementPlaybackStarted(handle.DangerousGetHandle());
            }
        }

        [MonoPInvokeCallback(typeof(JSNative.JSDelegate))]
        private static void OnHtmlAudioStopped(IntPtr iptr)
        {
            using (var handle = new JSHandle(iptr, true))
            {
                if (!TryGetStream(handle.DangerousGetHandle(), out WebGLAudioStream stream))
                    return;

                stream.MarkElementPlaybackStopped(handle.DangerousGetHandle());
            }
        }

        private static bool TryGetStream(IntPtr elementHandle, out WebGLAudioStream stream)
        {
            lock (s_streamsByElementLock)
            {
                if (s_streamsByElement.TryGetValue(elementHandle, out WeakReference<WebGLAudioStream> reference) &&
                    reference.TryGetTarget(out stream) &&
                    stream != null)
                    return true;

                s_streamsByElement.Remove(elementHandle);
            }

            stream = null;
            return false;
        }

        private void EnsurePlaybackTrackingInitialized()
        {
            if (_disposed || _playbackTrackingInitialized)
                return;

            _playbackTrackingInitialized = true;
            _track.ElementAttached += OnTrackElementAttached;
            _track.ElementDetached += OnTrackElementDetached;

            foreach (HTMLMediaElement element in _track.AttachedElements)
                RegisterAttachedElement(element);

            if (_registeredElements.Count > 0)
                _isActive = true;
        }

        private void TeardownPlaybackTracking()
        {
            if (!_playbackTrackingInitialized)
                return;

            _track.ElementAttached -= OnTrackElementAttached;
            _track.ElementDetached -= OnTrackElementDetached;
            _playbackTrackingInitialized = false;
            ClearRegisteredElements();
        }

        private void OnTrackElementAttached(HTMLMediaElement element)
        {
            RegisterAttachedElement(element);
            _isActive = true;
        }

        private void OnTrackElementDetached(HTMLMediaElement element)
        {
            if (element == null)
                return;

            UnregisterElement(element.NativeHandle.DangerousGetHandle());
            _isActive = _registeredElements.Count > 0;
        }

        private void RegisterAttachedElement(HTMLMediaElement element)
        {
            var audioElement = element as HTMLAudioElement;
            if (audioElement == null)
                return;

            IntPtr elementHandle = audioElement.NativeHandle.DangerousGetHandle();
            if (_registeredElements.ContainsKey(elementHandle))
                return;

            lock (s_streamsByElementLock)
                s_streamsByElement[elementHandle] = new WeakReference<WebGLAudioStream>(this);

            JSRef playingListener =
                audioElement.AddEventListener("playing", OnHtmlAudioPlaying, audioElement.NativeHandle);
            JSRef pauseListener = audioElement.AddEventListener("pause", OnHtmlAudioStopped, audioElement.NativeHandle);
            JSRef endedListener = audioElement.AddEventListener("ended", OnHtmlAudioStopped, audioElement.NativeHandle);
            _registeredElements[elementHandle] =
                new ElementRegistration(audioElement, playingListener, pauseListener, endedListener);

            if (_timingElementHandle == IntPtr.Zero)
                ConfigureTimingProbe(audioElement);
            SyncPlaybackState(audioElement);
        }

        private void SyncPlaybackState(HTMLAudioElement audioElement)
        {
            IntPtr elementHandle = audioElement.NativeHandle.DangerousGetHandle();
            if (ElementPlayingEvaluator(audioElement))
                MarkElementPlaybackStarted(elementHandle);
            else
                MarkElementPlaybackStopped(elementHandle);
        }

        private void ClearRegisteredElements()
        {
            var elementHandles = new IntPtr[_registeredElements.Count];
            _registeredElements.Keys.CopyTo(elementHandles, 0);

            foreach (IntPtr elementHandle in elementHandles)
                UnregisterElement(elementHandle);
        }

        private void UnregisterElement(IntPtr elementHandle)
        {
            if (!_registeredElements.TryGetValue(elementHandle, out ElementRegistration registration))
                return;

            registration.Element.RemoveEventListener("playing", registration.PlayingListener);
            registration.Element.RemoveEventListener("pause", registration.PauseListener);
            registration.Element.RemoveEventListener("ended", registration.EndedListener);
            _registeredElements.Remove(elementHandle);

            if (_timingElementHandle == elementHandle)
                SelectReplacementTimingElement();

            lock (s_streamsByElementLock)
                s_streamsByElement.Remove(elementHandle);

            MarkElementPlaybackStopped(elementHandle);
        }

        private void ConfigureTimingProbe(HTMLAudioElement audioElement)
        {
            if (audioElement == null) return;

            IntPtr elementHandle = audioElement.NativeHandle.DangerousGetHandle();
            if (_timingProbeId == 0)
            {
                _timingProbeId = TimingProbeCreator?.Invoke(audioElement) ?? 0;
                if (_timingProbeId == 0) return;
            }
            else if (_timingElementHandle != elementHandle)
            {
                TimingProbeElementUpdater?.Invoke(_timingProbeId, audioElement);
            }

            _timingElementHandle = elementHandle;
            _timingSnapshotCached = false;
        }

        private void SelectReplacementTimingElement()
        {
            _timingElementHandle = IntPtr.Zero;
            _timingSnapshotCached = false;
            foreach (ElementRegistration registration in _registeredElements.Values)
            {
                ConfigureTimingProbe(registration.Element);
                return;
            }

            if (_timingProbeId == 0) return;

            TimingProbeDisposer?.Invoke(_timingProbeId);
            _timingProbeId = 0;
        }

        private void MarkElementPlaybackStarted(IntPtr elementHandle)
        {
            if (_disposed || !_registeredElements.ContainsKey(elementHandle))
                return;

            _timingSnapshotCached = false;
            if (!_playingElementHandles.Add(elementHandle))
                return;

            if (_playingElementHandles.Count != 1)
                return;
            _playbackStarted?.Invoke();
        }

        private void MarkElementPlaybackStopped(IntPtr elementHandle)
        {
            _timingSnapshotCached = false;
            if (!_playingElementHandles.Remove(elementHandle))
                return;

            if (_playingElementHandles.Count != 0)
                return;
            _playbackStopped?.Invoke();
        }

        private static bool IsElementPlaying(HTMLAudioElement audioElement)
        {
            JSNative.PushString("paused");
            bool isPaused = JSNative.GetBoolean(JSNative.GetProperty(audioElement.NativeHandle));
            if (isPaused)
                return false;

            JSNative.PushString("ended");
            bool isEnded = JSNative.GetBoolean(JSNative.GetProperty(audioElement.NativeHandle));
            return !isEnded;
        }

        internal static bool ShouldInvokePlaybackStartedImmediately(bool wasTrackingInitialized,
            int playingElementCount) =>
            wasTrackingInitialized && playingElementCount > 0;

        internal static void ResumeTimingContext() => TimingContextResumer?.Invoke();

        internal static void ResetTestHooks()
        {
            ElementPlayingEvaluator = IsElementPlaying;
            TimingProbeCreator = CreateTimingProbe;
            TimingProbeElementUpdater = UpdateTimingProbeElement;
            TimingProbeReader = ReadTimingProbe;
            TimingProbeDisposer = DisposeTimingProbe;
            TimingProbeGainSetter = SetTimingProbeGain;
            TimingContextResumer = ResumeTimingContextInternal;
        }

        private void ApplyPlaybackGain(float targetGain, float durationSeconds)
        {
            if (_disposed || _timingProbeId == 0)
                return;

            TimingProbeGainSetter?.Invoke(
                _timingProbeId,
                Mathf.Clamp01(targetGain),
                Mathf.Max(0f, durationSeconds));
        }

        private static int CreateTimingProbe(HTMLAudioElement element)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return ConvaiWebGLAudioTiming_Create(element.NativeHandle.DangerousGetHandle());
#else
            return 0;
#endif
        }

        private static void UpdateTimingProbeElement(int probeId, HTMLAudioElement element)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            ConvaiWebGLAudioTiming_SetElement(probeId, element.NativeHandle.DangerousGetHandle());
#endif
        }

        private static AudioMediaTimelineSnapshot? ReadTimingProbe(int probeId)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            int read = ConvaiWebGLAudioTiming_Read(
                probeId,
                out double positionSeconds,
                out double signalStartSeconds,
                out int signalGeneration,
                out int discontinuityGeneration,
                out int playbackState,
                out int analyserAvailable,
                out int stallCount,
                out int elementReplacementCount);
            if (read == 0) return null;

            var state = playbackState >= (int)AudioTimelinePlaybackState.Idle &&
                        playbackState <= (int)AudioTimelinePlaybackState.Disposed
                ? (AudioTimelinePlaybackState)playbackState
                : AudioTimelinePlaybackState.Idle;
            return new AudioMediaTimelineSnapshot(
                positionSeconds,
                state,
                signalStartSeconds,
                signalGeneration,
                discontinuityGeneration,
                analyserAvailable != 0,
                stallCount,
                elementReplacementCount);
#else
            return null;
#endif
        }

        private static void DisposeTimingProbe(int probeId)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            ConvaiWebGLAudioTiming_Dispose(probeId);
#endif
        }

        private static void SetTimingProbeGain(int probeId, float targetGain, float durationSeconds)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            ConvaiWebGLAudioTiming_SetGain(
                probeId,
                Mathf.Clamp01(targetGain),
                Mathf.Max(0f, durationSeconds) * 1000f);
#endif
        }

        private static void ResumeTimingContextInternal()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            ConvaiWebGLAudioTiming_ResumeContext();
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void ConvaiWebGLAudioTiming_ResumeContext();

        [DllImport("__Internal")]
        private static extern int ConvaiWebGLAudioTiming_Create(IntPtr elementPtr);

        [DllImport("__Internal")]
        private static extern void ConvaiWebGLAudioTiming_SetElement(int probeId, IntPtr elementPtr);

        [DllImport("__Internal")]
        private static extern int ConvaiWebGLAudioTiming_Read(
            int probeId,
            out double positionSeconds,
            out double signalStartSeconds,
            out int signalGeneration,
            out int discontinuityGeneration,
            out int playbackState,
            out int analyserAvailable,
            out int stallCount,
            out int elementReplacementCount);

        [DllImport("__Internal")]
        private static extern void ConvaiWebGLAudioTiming_SetGain(
            int probeId,
            float targetGain,
            float durationMilliseconds);

        [DllImport("__Internal")]
        private static extern void ConvaiWebGLAudioTiming_Dispose(int probeId);
#endif

        #endregion
    }
}
