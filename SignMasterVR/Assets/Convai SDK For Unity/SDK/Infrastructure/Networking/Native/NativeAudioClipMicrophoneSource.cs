using System;
using LiveKit;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Convai.Infrastructure.Networking.Native
{
    /// <summary>
    ///     Test-friendly local audio source that publishes a pre-recorded <see cref="AudioClip" />
    ///     through the same audio-track path used by native microphone capture.
    /// </summary>
    public sealed class NativeAudioClipMicrophoneSource : IMicrophoneSource
    {
        private readonly bool _ownsSourceObject;
        private readonly GameObject _sourceObject;
        private readonly AudioSource _audioSource;
        private bool _disposed;
        private bool _isMuted;

        public NativeAudioClipMicrophoneSource(AudioClip clip, GameObject hostObject = null, string sourceName = null)
        {
            if (clip == null)
                throw new ArgumentNullException(nameof(clip));

            DeviceName = string.IsNullOrWhiteSpace(sourceName) ? clip.name : sourceName.Trim();
            DeviceIndex = 0;

            string hostSuffix = hostObject != null ? $" @ {hostObject.name}" : string.Empty;
            _sourceObject = new GameObject($"[Convai] AudioClipSource ({DeviceName}){hostSuffix}");
            _sourceObject.hideFlags = HideFlags.HideAndDontSave;
            Object.DontDestroyOnLoad(_sourceObject);
            _ownsSourceObject = true;

            _audioSource = _sourceObject.AddComponent<AudioSource>();
            _audioSource.clip = clip;
            _audioSource.loop = false;
            _audioSource.playOnAwake = false;
            _audioSource.spatialBlend = 0f;

            // AudioProbe captures the mixer/output channel layout, which is typically stereo even for mono clips.
            // Match the declared channel count to the Unity output path so LiveKit does not reject frames.
            UnderlyingSource = new BasicAudioSource(_audioSource, (int)RtcAudioSource.DefaultChannels);
        }

        public BasicAudioSource UnderlyingSource { get; }

        public string Name => DeviceName;

        public bool IsCapturing { get; private set; }

        public string DeviceName { get; }

        public int DeviceIndex { get; }

        public event Action<IMicrophoneSource, MicrophoneSessionInvalidationReason> SessionInvalidated;

        public bool IsMuted
        {
            get => _isMuted;
            set
            {
                _isMuted = value;
                UnderlyingSource?.SetMute(value);
            }
        }

        public bool EnableAcousticEchoCancellation { get; set; }

        public void StartCapture()
        {
            ThrowIfDisposed();

            if (IsCapturing)
                return;

            _audioSource.Stop();
            _audioSource.time = 0f;
            UnderlyingSource.Start();
            UnderlyingSource.SetMute(_isMuted);
            IsCapturing = true;
        }

        public void StopCapture()
        {
            if (!IsCapturing)
                return;

            try
            {
                UnderlyingSource.Stop();
            }
            finally
            {
                _audioSource.Stop();
                IsCapturing = false;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            StopCapture();
            UnderlyingSource?.Dispose();
            SessionInvalidated = null;

            if (_ownsSourceObject)
                Object.Destroy(_sourceObject);

            _disposed = true;
            GC.SuppressFinalize(this);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(NativeAudioClipMicrophoneSource));
        }
    }
}
