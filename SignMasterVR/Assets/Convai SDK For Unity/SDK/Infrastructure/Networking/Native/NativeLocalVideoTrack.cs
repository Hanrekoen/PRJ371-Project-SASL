using System;
using Convai.Infrastructure.Networking.Transport;
using LiveKit;

namespace Convai.Infrastructure.Networking.Native
{
    internal sealed class NativeLocalVideoTrack : ILocalVideoTrack
    {
        private readonly NativeTextureVideoSource _source;

        public NativeLocalVideoTrack(LocalVideoTrack track, NativeTextureVideoSource source)
        {
            UnderlyingTrack = track ?? throw new ArgumentNullException(nameof(track));
            _source = source ?? throw new ArgumentNullException(nameof(source));
            IsPublished = true;
        }

        internal LocalVideoTrack UnderlyingTrack { get; }

        public string Sid => UnderlyingTrack.Sid;

        public string Name => UnderlyingTrack.Name;

        public TrackKind Kind => TrackKind.Video;

        public bool IsMuted => UnderlyingTrack.Muted;

        public event Action<bool> MuteChanged;

        public IVideoSource Source => _source;

        public bool IsPublished { get; private set; }

        public void SetMuted(bool muted)
        {
            ((LiveKit.ILocalTrack)UnderlyingTrack).SetMute(muted);
            MuteChanged?.Invoke(muted);
        }

        internal void MarkUnpublished() => IsPublished = false;
    }
}
