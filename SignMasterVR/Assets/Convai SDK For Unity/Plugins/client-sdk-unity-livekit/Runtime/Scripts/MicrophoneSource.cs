using System;
using System.Collections;
using System.Threading;
using UnityEngine;
using LiveKit.Internal;

namespace LiveKit
{
    /// <summary>
    /// An audio source which captures from the device's microphone.
    /// </summary>
    /// <remarks>
    /// Ensure microphone permissions are granted before calling <see cref="Start"/>.
    /// </remarks>
    public sealed class MicrophoneSource : RtcAudioSource
    {
        private const float AndroidAecStartupHealthCheckDelaySeconds = 0.5f;
        private const float AndroidSilentSignalThreshold = 0.000001f;

        private readonly bool _enableAcousticEchoCancellation;
        private readonly GameObject _sourceObject;
        private readonly string _deviceName;
        private string _activeDeviceName;

        public override event Action<float[], int, int> AudioRead;

        private bool _disposed = false;
        private bool _started = false;
        private int _captureAttemptId;
        private int _audioReadCountThisAttempt;
        private volatile bool _isMonitoringAndroidAecStartup;
        private bool _performedAndroidAecStartupRecovery;
        private volatile float _latestAudioPeak;
        private float[] _monoBuffer;

        /// <summary>
        /// Creates a new microphone source for the given device.
        /// </summary>
        /// <param name="deviceName">The name of the device to capture from. Use <see cref="Microphone.devices"/> to
        /// get the list of available devices.</param>
        /// <param name="sourceObject">The GameObject to attach the AudioSource to. The object must be kept in the scene
        /// for the duration of the source's lifetime.</param>
        /// <param name="enableAcousticEchoCancellation">Whether to enable explicit APM-based acoustic echo cancellation.</param>
        public MicrophoneSource(
            string deviceName,
            GameObject sourceObject,
            bool enableAcousticEchoCancellation = false)
            : base(
                RtcAudioSourceType.AudioSourceMicrophone,
                (uint)ResolveUnityCaptureSampleRate(),
                1,
                enableAcousticEchoCancellation)
        {
            _enableAcousticEchoCancellation = enableAcousticEchoCancellation;
            _deviceName = deviceName;
            _sourceObject = sourceObject;
        }

        /// <summary>
        /// Begins capturing audio from the microphone.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the microphone is not available or unauthorized.
        /// </exception>
        /// <remarks>
        /// Ensure microphone permissions are granted before calling this method
        /// by calling <see cref="Application.RequestUserAuthorization"/>.
        /// </remarks>
        public override void Start()
        {
            base.Start();
            if (_started) return;


            if (!Application.HasUserAuthorization(mode: UserAuthorization.Microphone))
                throw new InvalidOperationException("Microphone access not authorized");

            _isMonitoringAndroidAecStartup = false;
            _performedAndroidAecStartupRecovery = false;
            MonoBehaviourContext.OnApplicationPauseEvent += OnApplicationPause;
            MonoBehaviourContext.RunCoroutine(StartMicrophone());

            _started = true;
        }

        private IEnumerator StartMicrophone()
        {
            int attemptId = ++_captureAttemptId;
            _audioReadCountThisAttempt = 0;
            _latestAudioPeak = 0f;

            // Validate that the GameObject is still valid before starting
            if (_sourceObject == null)
            {
                Utils.Error("MicrophoneSource: GameObject is null, cannot start microphone");
                yield break;
            }

            // Verify microphone is still authorized (could change during background)
            if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
            {
                Utils.Error("MicrophoneSource: Microphone authorization lost");
                yield break;
            }

            string resolvedDeviceName = ResolveCaptureDeviceName(_deviceName);
            int sampleRate = ResolveUnityCaptureSampleRate();
            AudioClip clip = null;
            try
            {
                clip = Microphone.Start(
                    resolvedDeviceName,
                    loop: true,
                    lengthSec: 1,
                    frequency: sampleRate
                );
            }
            catch (ArgumentException) when (!string.IsNullOrWhiteSpace(resolvedDeviceName))
            {
                resolvedDeviceName = null;
                clip = Microphone.Start(
                    resolvedDeviceName,
                    loop: true,
                    lengthSec: 1,
                    frequency: sampleRate
                );
            }
            catch (Exception e)
            {
                Utils.Error($"MicrophoneSource: Exception starting microphone: {e.Message}");
                yield break;
            }

            if (clip == null)
            {
                Utils.Error("MicrophoneSource: Microphone.Start returned null, audio session may not be ready");
                yield break;
            }

            _activeDeviceName = resolvedDeviceName;

            // Ensure no duplicate components exist before adding new ones.
            // This is important during app resume on iOS where components might not be
            // fully destroyed yet due to Unity's deferred Destroy().
            var existingSource = _sourceObject.GetComponent<AudioSource>();
            if (existingSource != null)
                UnityEngine.Object.DestroyImmediate(existingSource);

            var existingProbe = _sourceObject.GetComponent<AudioProbe>();
            if (existingProbe != null)
            {
                existingProbe.AudioRead -= OnAudioRead;
                UnityEngine.Object.DestroyImmediate(existingProbe);
            }

            var source = _sourceObject.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;

            var probe = _sourceObject.AddComponent<AudioProbe>();
            // Clear the audio data after it is read as to not play it through the speaker locally.
            probe.ClearAfterInvocation();
            probe.AudioRead += OnAudioRead;

            // Wait for microphone to actually start producing data with a timeout
            const float timeout = 2f;
            float elapsed = 0f;
            while (Microphone.GetPosition(_activeDeviceName) <= 0 && elapsed < timeout)
            {
                yield return new WaitForSeconds(0.05f);
                elapsed += 0.05f;
            }

            if (Microphone.GetPosition(_activeDeviceName) <= 0)
            {
                Utils.Error($"MicrophoneSource: Microphone did not start producing data after {timeout}s");
                yield break;
            }

            bool shouldMonitorAndroidAecStartup = ShouldPerformAndroidAecStartupRecovery(attemptId);
            _isMonitoringAndroidAecStartup = shouldMonitorAndroidAecStartup;
            source.Play();
            if (shouldMonitorAndroidAecStartup)
                MonoBehaviourContext.RunCoroutine(MonitorAndroidAecStartupRecovery(attemptId));

            Utils.Debug($"MicrophoneSource device='{_activeDeviceName ?? "default"}' started successfully");
        }

        /// <summary>
        /// Stops capturing audio from the microphone.
        /// </summary>
        public override void Stop()
        {
            base.Stop();
            _isMonitoringAndroidAecStartup = false;
            MonoBehaviourContext.RunCoroutine(StopMicrophone());
            MonoBehaviourContext.OnApplicationPauseEvent -= OnApplicationPause;
            _started = false;
        }

        private IEnumerator StopMicrophone()
        {
            if (string.IsNullOrWhiteSpace(_activeDeviceName))
            {
                Microphone.End(null);
            }
            else if (Microphone.IsRecording(_activeDeviceName))
            {
                Microphone.End(_activeDeviceName);
            }

            _activeDeviceName = null;

            // Check if GameObject is still valid before trying to access components
            if (_sourceObject != null)
            {
                var probe = _sourceObject.GetComponent<AudioProbe>();
                if (probe != null)
                {
                    probe.AudioRead -= OnAudioRead;
                    UnityEngine.Object.Destroy(probe);
                }

                var source = _sourceObject.GetComponent<AudioSource>();
                if (source != null)
                    UnityEngine.Object.Destroy(source);
            }

            Utils.Debug($"MicrophoneSource device='{_deviceName ?? "default"}' stopped");
            yield return null;
        }

        private void OnAudioRead(float[] data, int channels, int sampleRate)
        {
            float[] captureData = data;
            int captureChannels = channels;
            if (channels > 1 && data != null && data.Length % channels == 0)
            {
                int frameCount = data.Length / channels;
                if (_monoBuffer == null || _monoBuffer.Length != frameCount)
                    _monoBuffer = new float[frameCount];

                float channelScale = 1f / channels;
                for (int frame = 0; frame < frameCount; frame++)
                {
                    float mixedSample = 0f;
                    int frameStart = frame * channels;
                    for (int channel = 0; channel < channels; channel++)
                        mixedSample += data[frameStart + channel];

                    _monoBuffer[frame] = mixedSample * channelScale;
                }

                captureData = _monoBuffer;
                captureChannels = 1;
            }

            if (_isMonitoringAndroidAecStartup)
            {
                float peak = 0f;
                for (int i = 0; i < captureData.Length; i++)
                {
                    float abs = Mathf.Abs(captureData[i]);
                    if (abs > peak)
                        peak = abs;
                }

                _latestAudioPeak = peak;
                Interlocked.Increment(ref _audioReadCountThisAttempt);
            }

            AudioRead?.Invoke(captureData, captureChannels, sampleRate);
        }

        private void OnApplicationPause(bool pause)
        {
            if (!_started)
                return;

            if (pause)
            {
                // On iOS, when app goes to background, we should stop using audio resources
                // to avoid AVAudioSession interruption errors (FigCaptureSourceRemote -17281)
                MonoBehaviourContext.RunCoroutine(StopMicrophone());
            }
            else
            {
                // When resuming, restart the microphone
                MonoBehaviourContext.RunCoroutine(RestartMicrophone());
            }
        }

        private IEnumerator RestartMicrophone()
        {
            yield return StopMicrophone();

            // Wait for iOS audio session to be ready before attempting to restart.
            // On iOS, after app resumes from background, the audio session needs time to
            // recover from interruption. Poll for readiness instead of using arbitrary delay.
            yield return WaitForMicrophoneReady();

            yield return StartMicrophone();
        }

        private IEnumerator WaitForMicrophoneReady()
        {
            // Wait for microphone devices to become available again after iOS audio session interruption.
            // This is more reliable than a fixed delay because we wait for actual system readiness.
            const float timeout = 2f;
            float elapsed = 0f;

            // On iOS, Microphone.devices may be empty immediately after resume while
            // AVAudioSession is recovering from interruption. Wait until devices are available.
            while (Microphone.devices.Length == 0 && elapsed < timeout)
            {
                yield return new WaitForSeconds(0.05f);
                elapsed += 0.05f;
            }

            if (Microphone.devices.Length == 0)
            {
                Utils.Error($"MicrophoneSource: Microphone devices not available after {timeout}s timeout");
                yield break;
            }

            // Extra frame to ensure audio session is fully ready
            yield return null;
        }

        // Some Android devices start the first AEC-backed capture session with silent PCM until the mic is restarted.
        private IEnumerator MonitorAndroidAecStartupRecovery(int attemptId)
        {
            yield return new WaitForSecondsRealtime(AndroidAecStartupHealthCheckDelaySeconds);

            if (!ShouldPerformAndroidAecStartupRecovery(attemptId))
            {
                _isMonitoringAndroidAecStartup = false;
                yield break;
            }

            bool shouldRestart =
                Volatile.Read(ref _audioReadCountThisAttempt) > 0 &&
                _latestAudioPeak <= AndroidSilentSignalThreshold;
            _isMonitoringAndroidAecStartup = false;
            if (!shouldRestart)
                yield break;

            _performedAndroidAecStartupRecovery = true;
            Debug.LogWarning(
                "[LiveKit] Android microphone started silently with AEC enabled. Restarting capture once.");
            yield return RestartMicrophone();
        }

        private static string ResolveCaptureDeviceName(string requestedDeviceName)
        {
            if (string.IsNullOrWhiteSpace(requestedDeviceName))
                return null;

            string[] devices = Microphone.devices;
            if (devices == null || devices.Length == 0)
                return null;

            for (int i = 0; i < devices.Length; i++)
                if (string.Equals(devices[i], requestedDeviceName, StringComparison.Ordinal))
                    return requestedDeviceName;

            for (int i = 0; i < devices.Length; i++)
                if (string.Equals(devices[i], requestedDeviceName, StringComparison.OrdinalIgnoreCase))
                    return devices[i];

            return null;
        }

        private bool ShouldPerformAndroidAecStartupRecovery(int attemptId) =>
            _started &&
            attemptId == _captureAttemptId &&
            attemptId == 1 &&
            !_performedAndroidAecStartupRecovery &&
            _enableAcousticEchoCancellation &&
            Application.platform == RuntimePlatform.Android;

        protected override void Dispose(bool disposing)
        {
            if (!_disposed && disposing) Stop();
            _disposed = true;
            base.Dispose(disposing);
        }

        ~MicrophoneSource()
        {
            Dispose(false);
        }
    }
}
