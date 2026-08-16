using System;
using Convai.Infrastructure.Networking.Transport;
using LiveKit;
using UnityEngine;

#pragma warning disable CS0067

namespace Convai.Infrastructure.Networking.Native
{
    internal sealed class NativeRemoteAudioTrack : IRemoteAudioTrack, IRemoteAudioControlTrack
    {
        private readonly NativeRemoteParticipant _participant;
        private NativeAudioStream _attachedStream;

        public NativeRemoteAudioTrack(RemoteAudioTrack track, NativeRemoteParticipant participant)
        {
            UnderlyingTrack = track ?? throw new ArgumentNullException(nameof(track));
            _participant = participant ?? throw new ArgumentNullException(nameof(participant));
            IsSubscribed = true;
        }

        public RemoteAudioTrack UnderlyingTrack { get; }

        public void SetRemoteAudioEnabled(bool enabled) => ((LiveKit.IRemoteTrack)UnderlyingTrack).SetEnabled(enabled);

        public string Sid => UnderlyingTrack.Sid;

        public string Name => UnderlyingTrack.Name;

        public TrackKind Kind => TrackKind.Audio;

        public bool IsMuted => UnderlyingTrack.Muted;

        public event Action<bool> MuteChanged;

        public IRemoteParticipant Participant => _participant;

        public bool IsSubscribed { get; private set; }

        public IAudioStream CreateAudioStream() => new NativeAudioStream(UnderlyingTrack);

        public void AttachToAudioSource(AudioSource audioSource)
        {
            if (audioSource == null) throw new ArgumentNullException(nameof(audioSource));

            Detach();
            _attachedStream = new NativeAudioStream(UnderlyingTrack);
            _attachedStream.AttachToAudioSource(audioSource);
        }

        public void Detach()
        {
            if (_attachedStream == null) return;

            _attachedStream.Dispose();
            _attachedStream = null;
        }

        public bool IsAttached => _attachedStream != null;

        internal void SetSubscribed(bool subscribed)
        {
            IsSubscribed = subscribed;

            if (!subscribed)
            {
                // Ensure any attached playback stream is released when the subscription ends.
                Detach();
            }
        }
    }
}
