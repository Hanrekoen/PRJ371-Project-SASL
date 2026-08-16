using System;
using System.Collections.Generic;
using System.Linq;
using LiveKit;
using UnityEngine;
// Type alias to disambiguate LiveKit types from abstraction interfaces
using TransportDisconnectReason = Convai.Infrastructure.Networking.Transport.DisconnectReason;
using TransportTrackKind = Convai.Infrastructure.Networking.Transport.TrackKind;

namespace Convai.Infrastructure.Networking.WebGL
{
    /// <summary>
    ///     WebGL implementation of <see cref="IRoomFacade" /> wrapping the LiveKit WebGL Room.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Key differences from NativeRoomFacade:
    ///         <list type="bullet">
    ///             <item>
    ///                 <description>Uses coroutine-based async patterns instead of Task-based</description>
    ///             </item>
    ///             <item>
    ///                 <description>Audio plays through browser audio elements, not Unity AudioSource</description>
    ///             </item>
    ///             <item>
    ///                 <description>Requires user gesture for audio/microphone operations</description>
    ///             </item>
    ///             <item>
    ///                 <description>Some properties like Room.Sid and Room.ConnectionState are not available</description>
    ///             </item>
    ///         </list>
    ///     </para>
    /// </remarks>
    internal class WebGLRoomFacade : IRoomFacade, IDisposable
    {
        #region Constructor

        /// <summary>
        ///     Creates a new WebGL room facade wrapping a LiveKit room.
        /// </summary>
        /// <param name="room">The LiveKit room to wrap.</param>
        /// <param name="coroutineRunner">MonoBehaviour for running coroutines (used by LocalParticipant).</param>
        public WebGLRoomFacade(Room room, MonoBehaviour coroutineRunner)
        {
            UnderlyingRoom = room ?? throw new ArgumentNullException(nameof(room));
            if (coroutineRunner == null) throw new ArgumentNullException(nameof(coroutineRunner));

            // Create local participant wrapper
            if (UnderlyingRoom.LocalParticipant != null)
                _localParticipant = new WebGLLocalParticipant(UnderlyingRoom.LocalParticipant, coroutineRunner);

            // Create remote participant wrappers for existing participants
            foreach (RemoteParticipant participant in UnderlyingRoom.RemoteParticipants.Values)
            {
                var wrapper = new WebGLRemoteParticipant(participant);
                _remoteParticipants[participant.Sid] = wrapper;
            }

            // Subscribe to room events
            SubscribeToRoomEvents();
        }

        #endregion

        #region Internal Methods

        internal Room UnderlyingRoom { get; }

        #endregion

        #region Private Fields

        private readonly Dictionary<string, WebGLRemoteParticipant> _remoteParticipants = new();
        private IRemoteParticipant[] _remoteParticipantsSnapshot = Array.Empty<IRemoteParticipant>();
        private bool _remoteParticipantsDirty = true;
        private WebGLLocalParticipant _localParticipant;
        private bool _disposed;

        #endregion

        #region IRoomFacade Properties

        /// <inheritdoc />
        /// <remarks>On WebGL, the room SID may not be directly available. Returns an empty string.</remarks>
        public string Sid => string.Empty; // WebGL SDK doesn't expose Room.Sid

        /// <inheritdoc />
        public string Name => UnderlyingRoom.Name;

        /// <inheritdoc />
        public RoomState State { get; private set; } = RoomState.Connected;

        /// <inheritdoc />
        public bool IsConnected => State == RoomState.Connected;

        /// <inheritdoc />
        public ILocalParticipant LocalParticipant => _localParticipant;

        /// <inheritdoc />
        public IReadOnlyList<IRemoteParticipant> RemoteParticipants
        {
            get
            {
                if (!_remoteParticipantsDirty) return _remoteParticipantsSnapshot;

                int count = _remoteParticipants.Count;
                if (count == 0)
                {
                    _remoteParticipantsSnapshot = Array.Empty<IRemoteParticipant>();
                    _remoteParticipantsDirty = false;
                    return _remoteParticipantsSnapshot;
                }

                var snapshot = new IRemoteParticipant[count];
                int i = 0;
                foreach (WebGLRemoteParticipant participant in _remoteParticipants.Values) snapshot[i++] = participant;

                _remoteParticipantsSnapshot = snapshot;
                _remoteParticipantsDirty = false;
                return _remoteParticipantsSnapshot;
            }
        }

        /// <inheritdoc />
        public IEnumerable<IParticipant> AllParticipants
        {
            get
            {
                if (_localParticipant != null) yield return _localParticipant;
                foreach (WebGLRemoteParticipant participant in _remoteParticipants.Values) yield return participant;
            }
        }

        /// <inheritdoc />
        public int RemoteParticipantCount => _remoteParticipants.Count;

        #endregion

        #region IRoomFacade Participant Lookup

        /// <inheritdoc />
        public IRemoteParticipant GetParticipantBySid(string sid) =>
            _remoteParticipants.TryGetValue(sid, out WebGLRemoteParticipant participant) ? participant : null;

        /// <inheritdoc />
        public IRemoteParticipant GetParticipantByIdentity(string identity) =>
            _remoteParticipants.Values.FirstOrDefault(p => p.Identity == identity);

        /// <inheritdoc />
        public bool TryGetParticipantBySid(string sid, out IRemoteParticipant participant)
        {
            if (_remoteParticipants.TryGetValue(sid, out WebGLRemoteParticipant webglParticipant))
            {
                participant = webglParticipant;
                return true;
            }

            participant = null;
            return false;
        }

        /// <inheritdoc />
        public bool TryGetParticipantByIdentity(string identity, out IRemoteParticipant participant)
        {
            participant = GetParticipantByIdentity(identity);
            return participant != null;
        }

        #endregion

        #region IRoomFacade Events

        /// <inheritdoc />
        public event Action<IRemoteParticipant> ParticipantJoined;

        /// <inheritdoc />
        public event Action<IRemoteParticipant> ParticipantLeft;

        /// <inheritdoc />
#pragma warning disable CS0067 // Event is never used - required by interface
        public event Action<IParticipant, ParticipantMetadata> ParticipantMetadataUpdated;
#pragma warning restore CS0067

        /// <inheritdoc />
        public event Action<IRemoteAudioTrack, IRemoteParticipant> AudioTrackSubscribed;

        /// <inheritdoc />
        public event Action<IRemoteAudioTrack, IRemoteParticipant> AudioTrackUnsubscribed;

        /// <inheritdoc />
        public event Action<IRemoteVideoTrack, IRemoteParticipant> VideoTrackSubscribed;

        /// <inheritdoc />
        public event Action<IRemoteVideoTrack, IRemoteParticipant> VideoTrackUnsubscribed;

        /// <inheritdoc />
        public event Action<TrackSubscriptionEventArgs> TrackSubscribed;

        /// <inheritdoc />
        public event Action<TrackSubscriptionEventArgs> TrackUnsubscribed;

        /// <inheritdoc />
        public event Action<RoomState> StateChanged;

        /// <inheritdoc />
        public event Action Reconnecting;

        /// <inheritdoc />
        public event Action Reconnected;

        /// <inheritdoc />
        public event Action<TransportDisconnectReason> Disconnected;

        #endregion

        #region Event Subscriptions

        private void SubscribeToRoomEvents()
        {
            UnderlyingRoom.ParticipantConnected += OnParticipantConnected;
            UnderlyingRoom.ParticipantDisconnected += OnParticipantDisconnected;
            UnderlyingRoom.TrackSubscribed += OnTrackSubscribed;
            UnderlyingRoom.TrackUnsubscribed += OnTrackUnsubscribed;
            UnderlyingRoom.Reconnecting += OnReconnecting;
            UnderlyingRoom.Reconnected += OnReconnected;
            UnderlyingRoom.Disconnected += OnDisconnected;
        }

        internal void UnsubscribeFromRoomEvents()
        {
            UnderlyingRoom.ParticipantConnected -= OnParticipantConnected;
            UnderlyingRoom.ParticipantDisconnected -= OnParticipantDisconnected;
            UnderlyingRoom.TrackSubscribed -= OnTrackSubscribed;
            UnderlyingRoom.TrackUnsubscribed -= OnTrackUnsubscribed;
            UnderlyingRoom.Reconnecting -= OnReconnecting;
            UnderlyingRoom.Reconnected -= OnReconnected;
            UnderlyingRoom.Disconnected -= OnDisconnected;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                UnsubscribeFromRoomEvents();
            }
            catch
            {
                // Best-effort cleanup.
            }

            foreach (WebGLRemoteParticipant participant in _remoteParticipants.Values)
            {
                try
                {
                    participant.Dispose();
                }
                catch
                {
                    // Best-effort cleanup.
                }
            }

            _remoteParticipants.Clear();
            _remoteParticipantsSnapshot = Array.Empty<IRemoteParticipant>();
            _remoteParticipantsDirty = false;
            _localParticipant = null;

            GC.SuppressFinalize(this);
        }

        #endregion

        #region Event Handlers

        private void OnParticipantConnected(RemoteParticipant participant)
        {
            var wrapper = new WebGLRemoteParticipant(participant);

            if (_remoteParticipants.TryGetValue(participant.Sid, out WebGLRemoteParticipant existing))
            {
                try
                {
                    existing.Dispose();
                }
                catch
                {
                    // Best-effort cleanup.
                }
            }

            _remoteParticipants[participant.Sid] = wrapper;
            _remoteParticipantsDirty = true;
            ParticipantJoined?.Invoke(wrapper);
        }

        private void OnParticipantDisconnected(RemoteParticipant participant)
        {
            if (_remoteParticipants.TryGetValue(participant.Sid, out WebGLRemoteParticipant wrapper))
            {
                _remoteParticipants.Remove(participant.Sid);
                _remoteParticipantsDirty = true;
                ParticipantLeft?.Invoke(wrapper);

                try
                {
                    wrapper.Dispose();
                }
                catch
                {
                    // Best-effort cleanup.
                }
            }
        }

        private void OnTrackSubscribed(RemoteTrack track, RemoteTrackPublication publication,
            RemoteParticipant participant)
        {
            if (!_remoteParticipants.TryGetValue(participant.Sid, out WebGLRemoteParticipant participantWrapper))
                return;

            string trackName = publication.TrackName;
            bool isMuted = false; // WebGL SDK uses event for mute state

            if (track.Kind == TrackKind.Audio)
            {
                var trackWrapper = new WebGLRemoteAudioTrack(track, participantWrapper, trackName);
                participantWrapper.AddSubscribedTrack(trackWrapper);

                var pubInfo = new TrackPublicationInfo(track.Sid, trackName, TransportTrackKind.Audio, isMuted, true);
                AudioTrackSubscribed?.Invoke(trackWrapper, participantWrapper);
                TrackSubscribed?.Invoke(new TrackSubscriptionEventArgs(trackWrapper, participantWrapper, pubInfo));
            }
            else if (track.Kind == TrackKind.Video)
            {
                var trackWrapper = new WebGLRemoteVideoTrack(track, participantWrapper, trackName);
                participantWrapper.AddSubscribedTrack(trackWrapper);

                var pubInfo = new TrackPublicationInfo(track.Sid, trackName, TransportTrackKind.Video, isMuted, true);
                VideoTrackSubscribed?.Invoke(trackWrapper, participantWrapper);
                TrackSubscribed?.Invoke(new TrackSubscriptionEventArgs(trackWrapper, participantWrapper, pubInfo));
            }
        }

        private void OnTrackUnsubscribed(RemoteTrack track, RemoteTrackPublication publication,
            RemoteParticipant participant)
        {
            if (!_remoteParticipants.TryGetValue(participant.Sid, out WebGLRemoteParticipant participantWrapper))
                return;

            IRemoteTrack trackWrapper = participantWrapper.RemoveSubscribedTrack(track.Sid);
            if (trackWrapper == null) return;

            TransportTrackKind trackKind =
                track.Kind == TrackKind.Audio ? TransportTrackKind.Audio : TransportTrackKind.Video;
            var pubInfo = new TrackPublicationInfo(
                track.Sid,
                publication.TrackName,
                trackKind
            );

            if (trackWrapper is IRemoteAudioTrack audioTrack)
                AudioTrackUnsubscribed?.Invoke(audioTrack, participantWrapper);
            else if (trackWrapper is IRemoteVideoTrack videoTrack)
                VideoTrackUnsubscribed?.Invoke(videoTrack, participantWrapper);

            TrackUnsubscribed?.Invoke(new TrackSubscriptionEventArgs(trackWrapper, participantWrapper, pubInfo));
        }

        private void OnReconnecting()
        {
            State = RoomState.Reconnecting;
            StateChanged?.Invoke(RoomState.Reconnecting);
            Reconnecting?.Invoke();
        }

        private void OnReconnected()
        {
            State = RoomState.Connected;
            StateChanged?.Invoke(RoomState.Connected);
            Reconnected?.Invoke();
        }

        private void OnDisconnected(DisconnectReason? reason)
        {
            State = RoomState.Disconnected;
            StateChanged?.Invoke(RoomState.Disconnected);
            Disconnected?.Invoke(WebGLDisconnectReasonMapper.Map(reason));
        }

        #endregion
    }
}
