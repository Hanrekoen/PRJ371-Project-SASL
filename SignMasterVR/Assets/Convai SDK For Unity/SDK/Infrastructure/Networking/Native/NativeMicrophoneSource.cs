using System;
using Convai.Runtime.Networking.Media;
using LiveKit;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Convai.Infrastructure.Networking.Native
{
    internal sealed class NativeMicrophoneSource : IMicrophoneSource, IMicrophonePcmSource
    {
        private const float MonitoringIntervalSeconds = 0.5f;
        private readonly bool _ownsSourceObject;
        private readonly NativeMicrophoneSourceMonitor _monitor;
        private readonly GameObject _sourceObject;
        private readonly NativeVoiceProcessingSession _voiceProcessingSession;
        private string _lastDeviceListSignature;
        private int _lastRouteSignature;
        private bool _sessionInvalidated;
        private bool _disposed;
        private MicrophoneSource _underlyingSource;
        private bool _enableAcousticEchoCancellation;
        private Action<float[], int, int> _pcmFrame;
        private bool _processedAudioSubscribed;

        public NativeMicrophoneSource(string deviceName, int deviceIndex = 0, GameObject hostObject = null)
        {
            DeviceName = string.IsNullOrWhiteSpace(deviceName) ? null : deviceName.Trim();
            DeviceIndex = deviceIndex;
            string displayName = string.IsNullOrWhiteSpace(DeviceName) ? "System Default" : DeviceName;

            string hostSuffix = hostObject != null ? $" @ {hostObject.name}" : string.Empty;
            _sourceObject = new GameObject($"[Convai] MicrophoneSource ({displayName}){hostSuffix}");
            _sourceObject.hideFlags = HideFlags.HideAndDontSave;
            Object.DontDestroyOnLoad(_sourceObject);
            _ownsSourceObject = true;
            _monitor = _sourceObject.AddComponent<NativeMicrophoneSourceMonitor>();
            _voiceProcessingSession = new NativeVoiceProcessingSession();
        }

        public MicrophoneSource UnderlyingSource => _underlyingSource ??= new MicrophoneSource(DeviceName, _sourceObject,
            _enableAcousticEchoCancellation);

        public string Name => DeviceName;

        public bool IsCapturing { get; private set; }

        public string DeviceName { get; }

        public int DeviceIndex { get; }

        public event Action<IMicrophoneSource, MicrophoneSessionInvalidationReason> SessionInvalidated;
        public event Action<float[], int, int> PcmFrame
        {
            add
            {
                bool subscribeToSource = _pcmFrame == null;
                _pcmFrame += value;
                if (subscribeToSource)
                    SubscribeToProcessedAudio();
            }
            remove
            {
                _pcmFrame -= value;
                if (_pcmFrame == null)
                    UnsubscribeFromProcessedAudio();
            }
        }

        public bool IsAcousticEchoCancellationActive =>
            _enableAcousticEchoCancellation &&
            _underlyingSource?.IsAcousticEchoCancellationActive == true;

        public bool IsMuted
        {
            get => _underlyingSource?.Muted ?? false;
            set => _underlyingSource?.SetMute(value);
        }

        public bool EnableAcousticEchoCancellation
        {
            get => _enableAcousticEchoCancellation;
            set
            {
                ThrowIfDisposed();
                if (_underlyingSource != null || IsCapturing)
                    return;

                _enableAcousticEchoCancellation = value;
            }
        }

        public void StartCapture()
        {
            ThrowIfDisposed();

            if (IsCapturing) return;

            try
            {
                if (_enableAcousticEchoCancellation)
                    _voiceProcessingSession.Start();

                MicrophoneSource source = UnderlyingSource;
                SubscribeToProcessedAudio();
                source.Start();
                IsCapturing = true;
                BeginSessionMonitoring();
            }
            catch
            {
                UnsubscribeFromProcessedAudio();
                StopSessionMonitoring();
                _voiceProcessingSession.Stop();
                throw;
            }
        }

        public void StopCapture()
        {
            if (!IsCapturing) return;

            try
            {
                UnsubscribeFromProcessedAudio();
                _underlyingSource?.Stop();
            }
            finally
            {
                IsCapturing = false;
                StopSessionMonitoring();
                _voiceProcessingSession.Stop();

                // LiveKit's MicrophoneSource.Stop() ends capture in a coroutine, so Microphone.End may not have run yet.
                // A fast reconnect or second Play session can call Microphone.Start while the device is still recording,
                // which fails silently on some platforms until the editor process restarts.
                EnsureAllRecordingDevicesReleased();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            StopCapture();
            _underlyingSource?.Dispose();
            _voiceProcessingSession.Dispose();
            SessionInvalidated = null;
            _pcmFrame = null;

            if (_ownsSourceObject) Object.Destroy(_sourceObject);

            _disposed = true;
            GC.SuppressFinalize(this);
        }

        private void HandleProcessedAudioRead(float[] samples, int channels, int sampleRate) =>
            _pcmFrame?.Invoke(samples, channels, sampleRate);

        private void SubscribeToProcessedAudio()
        {
            if (_processedAudioSubscribed || _pcmFrame == null || _underlyingSource == null)
                return;

            _underlyingSource.ProcessedAudioRead += HandleProcessedAudioRead;
            _processedAudioSubscribed = true;
        }

        private void UnsubscribeFromProcessedAudio()
        {
            if (!_processedAudioSubscribed)
                return;

            if (_underlyingSource != null)
                _underlyingSource.ProcessedAudioRead -= HandleProcessedAudioRead;
            _processedAudioSubscribed = false;
        }

        /// <summary>
        ///     Best-effort synchronous release of Unity's microphone capture. Complements LiveKit's async stop coroutine.
        ///     Safe to call from any native teardown path that uses <see cref="LiveKit.MicrophoneSource" />.
        /// </summary>
        internal static void EnsureAllRecordingDevicesReleased()
        {
            try
            {
                Microphone.End(null);
                string[] devices = Microphone.devices;
                if (devices == null || devices.Length == 0) return;

                foreach (string device in devices)
                {
                    if (string.IsNullOrEmpty(device)) continue;
                    if (Microphone.IsRecording(device)) Microphone.End(device);
                }
            }
            catch
            {
                // Ignore — teardown must never throw; LiveKit will also attempt End in its coroutine.
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(NativeMicrophoneSource));
        }

        private void BeginSessionMonitoring()
        {
            _sessionInvalidated = false;
            _lastDeviceListSignature = BuildDeviceListSignature();
            _lastRouteSignature = ShouldMonitorRoutes
                ? _voiceProcessingSession.GetCurrentRouteSignature()
                : 0;
            _monitor.StartPolling(PollCaptureState, MonitoringIntervalSeconds);
        }

        private void StopSessionMonitoring()
        {
            _monitor.StopPolling();
            _sessionInvalidated = false;
        }

        private bool PollCaptureState()
        {
            if (_disposed || !IsCapturing || _sessionInvalidated)
                return false;

            if (HasCaptureDeviceChanged())
            {
                RaiseSessionInvalidated(MicrophoneSessionInvalidationReason.DeviceChanged);
                return false;
            }

            if (ShouldMonitorRoutes)
            {
                int currentRouteSignature = _voiceProcessingSession.GetCurrentRouteSignature();
                if (currentRouteSignature != _lastRouteSignature)
                {
                    RaiseSessionInvalidated(MicrophoneSessionInvalidationReason.RouteChanged);
                    return false;
                }
            }

            return true;
        }

        private bool HasCaptureDeviceChanged()
        {
            string currentSignature = BuildDeviceListSignature();
            if (string.Equals(currentSignature, _lastDeviceListSignature, StringComparison.Ordinal))
                return false;

            _lastDeviceListSignature = currentSignature;

            if (string.IsNullOrWhiteSpace(DeviceName))
                return true;

            string[] devices = Microphone.devices ?? Array.Empty<string>();
            for (int i = 0; i < devices.Length; i++)
            {
                if (string.Equals(devices[i], DeviceName, StringComparison.Ordinal) ||
                    string.Equals(devices[i], DeviceName, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return true;
        }

        private bool ShouldMonitorRoutes => _enableAcousticEchoCancellation && _voiceProcessingSession.SupportsRouteMonitoring;

        private static string BuildDeviceListSignature()
        {
            string[] devices = Microphone.devices ?? Array.Empty<string>();
            if (devices.Length == 0)
                return string.Empty;

            return string.Join("\u001f", devices);
        }

        private void RaiseSessionInvalidated(MicrophoneSessionInvalidationReason reason)
        {
            if (_sessionInvalidated)
                return;

            _sessionInvalidated = true;
            SessionInvalidated?.Invoke(this, reason);
        }
    }
}
