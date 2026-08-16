using System;
using System.Collections.Generic;
using System.Linq;
using Convai.Infrastructure.Networking.Transport;
using LiveKit;

#pragma warning disable CS0067

namespace Convai.Infrastructure.Networking.Native
{
    internal sealed class NativeRemoteParticipant : IRemoteParticipant, IDisposable
    {
        private readonly Dictionary<string, IRemoteTrack> _subscribedTracks = new();
        private bool _disposed;
        private bool _subscribedTracksDirty = true;
        private IRemoteTrack[] _subscribedTracksSnapshot = Array.Empty<IRemoteTrack>();

        public NativeRemoteParticipant(RemoteParticipant participant)
        {
            UnderlyingParticipant = participant ?? throw new ArgumentNullException(nameof(participant));
        }

        internal RemoteParticipant UnderlyingParticipant { get; }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            foreach (IRemoteTrack track in _subscribedTracks.Values)
            {
                try
                {
                    if (track is NativeRemoteAudioTrack nativeAudio)
                        nativeAudio.SetSubscribed(false);
                    else if (track is NativeRemoteVideoTrack nativeVideo)
                        nativeVideo.SetSubscribed(false);
                    else
                    {
                        (track as IRemoteAudioTrack)?.Detach();
                        (track as IRemoteVideoTrack)?.Detach();
                    }
                }
                catch
                {
                    // Best-effort cleanup.
                }
            }

            _subscribedTracks.Clear();
            _subscribedTracksSnapshot = Array.Empty<IRemoteTrack>();
            _subscribedTracksDirty = false;

            GC.SuppressFinalize(this);
        }

        public string Sid => UnderlyingParticipant.Sid;

        public string Identity => UnderlyingParticipant.Identity;

        public string Name => UnderlyingParticipant.Name;

        public ParticipantMetadata Metadata => new(
            UnderlyingParticipant.Metadata,
            UnderlyingParticipant.Name
        );

        public bool IsAgent => false;

        public event Action<ParticipantMetadata> MetadataUpdated;

        public IReadOnlyList<TrackPublicationInfo> TrackPublications => UnderlyingParticipant.Tracks.Values
            .Select(MapPublication)
            .ToList();

        public IReadOnlyList<IRemoteTrack> SubscribedTracks
        {
            get
            {
                if (!_subscribedTracksDirty) return _subscribedTracksSnapshot;

                int count = _subscribedTracks.Count;
                if (count == 0)
                {
                    _subscribedTracksSnapshot = Array.Empty<IRemoteTrack>();
                    _subscribedTracksDirty = false;
                    return _subscribedTracksSnapshot;
                }

                var snapshot = new IRemoteTrack[count];
                int i = 0;
                foreach (IRemoteTrack track in _subscribedTracks.Values) snapshot[i++] = track;

                _subscribedTracksSnapshot = snapshot;
                _subscribedTracksDirty = false;
                return _subscribedTracksSnapshot;
            }
        }

        public IEnumerable<IRemoteAudioTrack> AudioTracks => _subscribedTracks.Values.OfType<IRemoteAudioTrack>();

        public IEnumerable<IRemoteVideoTrack> VideoTracks => _subscribedTracks.Values.OfType<IRemoteVideoTrack>();

        public event Action<IRemoteTrack, TrackPublicationInfo> TrackSubscribed;

        public event Action<IRemoteTrack, TrackPublicationInfo> TrackUnsubscribed;

        public event Action<TrackPublicationInfo, bool> TrackMuteChanged;

        internal bool HasSubscribedTrack(string trackSid) =>
            !string.IsNullOrEmpty(trackSid) && _subscribedTracks.ContainsKey(trackSid);

        internal void AddSubscribedTrack(IRemoteTrack track)
        {
            if (track == null) return;

            _subscribedTracks[track.Sid] = track;
            _subscribedTracksDirty = true;
            var publication = new TrackPublicationInfo(track.Sid, track.Name, track.Kind, track.IsMuted, true);
            TrackSubscribed?.Invoke(track, publication);
        }

        internal IRemoteTrack RemoveSubscribedTrack(string trackSid)
        {
            if (string.IsNullOrEmpty(trackSid)) return null;

            if (!_subscribedTracks.TryGetValue(trackSid, out IRemoteTrack track)) return null;

            _subscribedTracks.Remove(trackSid);
            _subscribedTracksDirty = true;

            if (track is NativeRemoteAudioTrack audioTrack)
                audioTrack.SetSubscribed(false);
            else if (track is NativeRemoteVideoTrack videoTrack) videoTrack.SetSubscribed(false);

            var publication = new TrackPublicationInfo(track.Sid, track.Name, track.Kind, track.IsMuted);
            TrackUnsubscribed?.Invoke(track, publication);
            return track;
        }

        private static TrackPublicationInfo MapPublication(RemoteTrackPublication publication)
        {
            TrackKind kind = publication.Kind == LiveKit.Proto.TrackKind.KindAudio ? TrackKind.Audio : TrackKind.Video;
            return new TrackPublicationInfo(publication.Sid, publication.Name, kind, publication.Muted,
                publication.Subscribed);
        }
    }
}
