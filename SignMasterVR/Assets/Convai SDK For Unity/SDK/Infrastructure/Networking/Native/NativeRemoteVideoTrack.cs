using System;
using Convai.Infrastructure.Networking.Transport;
using LiveKit;
using UnityEngine;

#pragma warning disable CS0067

namespace Convai.Infrastructure.Networking.Native
{
    internal sealed class NativeRemoteVideoTrack : IRemoteVideoTrack
    {
        private readonly NativeRemoteParticipant _participant;
        private VideoStream _stream;
        private RenderTexture _target;
        private Coroutine _updateCoroutine;

        public NativeRemoteVideoTrack(RemoteVideoTrack track, NativeRemoteParticipant participant)
        {
            UnderlyingTrack = track ?? throw new ArgumentNullException(nameof(track));
            _participant = participant ?? throw new ArgumentNullException(nameof(participant));
            IsSubscribed = true;
        }

        internal RemoteVideoTrack UnderlyingTrack { get; }

        public string Sid => UnderlyingTrack.Sid;

        public string Name => UnderlyingTrack.Name;

        public TrackKind Kind => TrackKind.Video;

        public bool IsMuted => UnderlyingTrack.Muted;

        public event Action<bool> MuteChanged;

        public IRemoteParticipant Participant => _participant;

        public bool IsSubscribed { get; private set; }

        public void AttachToRenderTexture(RenderTexture target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));

            Detach();

            _target = target;
            _stream = new VideoStream(UnderlyingTrack);
            _stream.TextureReceived += OnTextureReceived;
            _stream.TextureUploaded += OnTextureUploaded;
            _stream.Start();
            _updateCoroutine = NativeCoroutineRunner.Run(_stream.Update());
        }

        public void Detach()
        {
            if (_stream == null)
            {
                _target = null;
                Dimensions = null;
                return;
            }

            _stream.TextureReceived -= OnTextureReceived;
            _stream.TextureUploaded -= OnTextureUploaded;
            _stream.Stop();
            NativeCoroutineRunner.Stop(_updateCoroutine);
            _updateCoroutine = null;
            _stream.Dispose();
            _stream = null;
            _target = null;
            Dimensions = null;
        }

        public bool IsAttached => _stream != null;

        public (int width, int height)? Dimensions { get; private set; }

        internal void SetSubscribed(bool subscribed)
        {
            IsSubscribed = subscribed;

            if (!subscribed)
            {
                // Ensure the video stream + coroutine are stopped when the subscription ends.
                Detach();
            }
        }

        private void OnTextureReceived(Texture texture)
        {
            if (texture == null) return;

            Dimensions = (texture.width, texture.height);
        }

        private void OnTextureUploaded()
        {
            if (_target == null || _stream?.Texture == null) return;

            Graphics.Blit(_stream.Texture, _target);
        }
    }
}
