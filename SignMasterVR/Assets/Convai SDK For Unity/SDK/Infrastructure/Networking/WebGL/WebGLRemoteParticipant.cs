using System;
using System.Collections.Generic;
using System.Linq;
using LiveKit;

// Type alias to disambiguate LiveKit types from abstraction interfaces

namespace Convai.Infrastructure.Networking.WebGL
{
    /// <summary>
    ///     WebGL implementation of <see cref="IRemoteParticipant" /> wrapping the LiveKit WebGL RemoteParticipant.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Key differences from NativeRemoteParticipant:
    ///         <list type="bullet">
    ///             <item>
    ///                 <description>TrackPublications are derived from subscribed tracks (WebGL SDK limitation)</description>
    ///             </item>
    ///             <item>
    ///                 <description>Some properties may not be immediately available</description>
    ///             </item>
    ///         </list>
    ///     </para>
    /// </remarks>
    internal sealed class WebGLRemoteParticipant : IRemoteParticipant, IDisposable
    {
        #region Constructor

        public WebGLRemoteParticipant(RemoteParticipant participant)
        {
            UnderlyingParticipant = participant ?? throw new ArgumentNullException(nameof(participant));
        }

        #endregion

        #region Private Fields

        private readonly Dictionary<string, IRemoteTrack> _subscribedTracks = new();
        private IRemoteTrack[] _subscribedTracksSnapshot = Array.Empty<IRemoteTrack>();
        private TrackPublicationInfo[] _trackPublicationsSnapshot = Array.Empty<TrackPublicationInfo>();
        private bool _trackSnapshotsDirty = true;
        private bool _disposed;

        #endregion

        #region IParticipant Properties

        /// <inheritdoc />
        public string Sid => UnderlyingParticipant.Sid;

        /// <inheritdoc />
        public string Identity => UnderlyingParticipant.Identity;

        /// <inheritdoc />
        public string Name => UnderlyingParticipant.Name;

        /// <inheritdoc />
        public ParticipantMetadata Metadata => new(
            UnderlyingParticipant.Metadata,
            UnderlyingParticipant.Name
        );

        /// <inheritdoc />
        public bool IsAgent => false;

        /// <inheritdoc />
        /// <remarks>
        ///     On WebGL, metadata update events are not currently supported by the underlying SDK.
        ///     This event is declared for interface compliance but will not fire.
        /// </remarks>
#pragma warning disable CS0067 // Event is never used
        public event Action<ParticipantMetadata> MetadataUpdated;
#pragma warning restore CS0067

        #endregion

        #region IRemoteParticipant Properties

        /// <inheritdoc />
        /// <remarks>
        ///     On WebGL, track publications are derived from currently subscribed tracks
        ///     since the SDK doesn't expose a Tracks dictionary.
        /// </remarks>
        public IReadOnlyList<TrackPublicationInfo> TrackPublications
        {
            get
            {
                EnsureTrackSnapshots();
                return _trackPublicationsSnapshot;
            }
        }

        /// <inheritdoc />
        public IReadOnlyList<IRemoteTrack> SubscribedTracks
        {
            get
            {
                EnsureTrackSnapshots();
                return _subscribedTracksSnapshot;
            }
        }

        /// <inheritdoc />
        public IEnumerable<IRemoteAudioTrack> AudioTracks => _subscribedTracks.Values.OfType<IRemoteAudioTrack>();

        /// <inheritdoc />
        public IEnumerable<IRemoteVideoTrack> VideoTracks => _subscribedTracks.Values.OfType<IRemoteVideoTrack>();

        #endregion

        #region IRemoteParticipant Events

        /// <inheritdoc />
        public event Action<IRemoteTrack, TrackPublicationInfo> TrackSubscribed;

        /// <inheritdoc />
        public event Action<IRemoteTrack, TrackPublicationInfo> TrackUnsubscribed;

        /// <inheritdoc />
        /// <remarks>
        ///     On WebGL, track mute change events are not currently forwarded from the underlying SDK.
        ///     This event is declared for interface compliance but will not fire.
        /// </remarks>
#pragma warning disable CS0067 // Event is never used
        public event Action<TrackPublicationInfo, bool> TrackMuteChanged;
#pragma warning restore CS0067

        #endregion

        #region Internal Methods

        /// <summary>
        ///     Adds a subscribed track to this participant.
        /// </summary>
        internal void AddSubscribedTrack(IRemoteTrack track)
        {
            if (track == null) return;

            _subscribedTracks[track.Sid] = track;
            _trackSnapshotsDirty = true;
            var publication = new TrackPublicationInfo(track.Sid, track.Name, track.Kind, track.IsMuted, true);
            TrackSubscribed?.Invoke(track, publication);
        }

        /// <summary>
        ///     Removes a subscribed track from this participant.
        /// </summary>
        internal IRemoteTrack RemoveSubscribedTrack(string trackSid)
        {
            if (string.IsNullOrEmpty(trackSid)) return null;

            if (!_subscribedTracks.TryGetValue(trackSid, out IRemoteTrack track)) return null;

            _subscribedTracks.Remove(trackSid);
            _trackSnapshotsDirty = true;

            if (track is WebGLRemoteAudioTrack audioTrack)
                audioTrack.SetSubscribed(false);
            else if (track is WebGLRemoteVideoTrack videoTrack) videoTrack.SetSubscribed(false);

            var publication = new TrackPublicationInfo(track.Sid, track.Name, track.Kind, track.IsMuted);
            TrackUnsubscribed?.Invoke(track, publication);
            return track;
        }

        private void EnsureTrackSnapshots()
        {
            if (!_trackSnapshotsDirty) return;

            int count = _subscribedTracks.Count;
            if (count == 0)
            {
                _subscribedTracksSnapshot = Array.Empty<IRemoteTrack>();
                _trackPublicationsSnapshot = Array.Empty<TrackPublicationInfo>();
                _trackSnapshotsDirty = false;
                return;
            }

            var tracks = new IRemoteTrack[count];
            var pubs = new TrackPublicationInfo[count];

            int i = 0;
            foreach (IRemoteTrack track in _subscribedTracks.Values)
            {
                tracks[i] = track;
                pubs[i] = new TrackPublicationInfo(track.Sid, track.Name, track.Kind, track.IsMuted, true);
                i++;
            }

            _subscribedTracksSnapshot = tracks;
            _trackPublicationsSnapshot = pubs;
            _trackSnapshotsDirty = false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            foreach (IRemoteTrack track in _subscribedTracks.Values)
            {
                try
                {
                    if (track is WebGLRemoteAudioTrack audio)
                        audio.SetSubscribed(false);
                    else if (track is WebGLRemoteVideoTrack video)
                        video.SetSubscribed(false);
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
            _trackPublicationsSnapshot = Array.Empty<TrackPublicationInfo>();
            _trackSnapshotsDirty = false;

            GC.SuppressFinalize(this);
        }

        internal RemoteParticipant UnderlyingParticipant { get; }

        #endregion
    }
}
