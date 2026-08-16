using System;
using LiveKit;
using UnityEngine;

#pragma warning disable CS0067

namespace Convai.Infrastructure.Networking.Native
{
    internal sealed class NativeAudioStream : IAudioStream
    {
        private readonly RemoteAudioTrack _track;
        private AudioSource _attachedSource;
        private bool _disposed;
        private AudioStream _stream;

        public NativeAudioStream(RemoteAudioTrack track)
        {
            _track = track ?? throw new ArgumentNullException(nameof(track));
        }

        public bool IsActive => _stream != null;

        public int SampleRate => AudioSettings.outputSampleRate;

        public int Channels => AudioSettings.speakerMode == AudioSpeakerMode.Mono ? 1 : 2;

        public event Action<float[], int, int> AudioDataReceived;

        public void AttachToAudioSource(AudioSource target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));

            ThrowIfDisposed();
            Detach();

            _attachedSource = target;
            _stream = new AudioStream(_track, target);
        }

        public void Detach()
        {
            if (_stream == null) return;

            _stream.Dispose();
            _stream = null;
            _attachedSource = null;
        }

        public void Dispose()
        {
            if (_disposed) return;

            Detach();
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(NativeAudioStream));
        }
    }
}
