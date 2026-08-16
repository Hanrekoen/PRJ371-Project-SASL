using System;
using Convai.Domain.Logging;
using LiveKit;
using LiveKit.Proto;
// Type alias to disambiguate LiveKit types from abstraction types
using LKRemoteTrack = LiveKit.IRemoteTrack;

namespace Convai.Infrastructure.Networking.Events
{
    /// <summary>
    ///     Implementation of IRoomEventDispatcher for dispatching room events.
    /// </summary>
    /// <remarks>
    ///     Extracted from room controller to adhere to Single Responsibility Principle.
    ///     Manages:
    ///     - Room event subscription/unsubscription
    ///     - Event forwarding to registered handlers
    ///     - Automatic cleanup on disposal
    ///     Thread-safe: Uses locking for concurrent access.
    /// </remarks>
    public sealed class RoomEventDispatcher : IDisposable
    {
        private readonly object _lock = new();
        private readonly ILogger _logger;
        private bool _disposed;

        private Room _room;

        public RoomEventDispatcher(ILogger logger = null)
        {
            _logger = logger.WithTag(nameof(RoomEventDispatcher));
        }

        /// <inheritdoc />
        public bool IsAttached
        {
            get
            {
                lock (_lock) return _room != null;
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed) return;

                _disposed = true;
                DetachFromRoomInternal();
            }
        }

        /// <inheritdoc />
        public event Action<ConnectionState> ConnectionStateChanged;

        /// <inheritdoc />
        public event Action<RemoteParticipant> ParticipantJoined;

        /// <inheritdoc />
        public event Action<RemoteParticipant> ParticipantLeft;

        /// <inheritdoc />
        public event Action<RemoteTrackPublication, RemoteParticipant> TrackPublished;

        /// <inheritdoc />
        public event Action<RemoteTrackPublication, RemoteParticipant> TrackUnpublished;

        /// <inheritdoc />
        public event Action<LKRemoteTrack, RemoteTrackPublication, RemoteParticipant> TrackSubscribed;

        /// <inheritdoc />
        public event Action<LKRemoteTrack, RemoteTrackPublication, RemoteParticipant> TrackUnsubscribed;

        /// <inheritdoc />
        public event Action<byte[], Participant, DataPacketKind, string> DataReceived;

        /// <inheritdoc />
        public event Action<Room> Disconnected;

        /// <inheritdoc />
        public event Action Reconnecting;

        /// <inheritdoc />
        public event Action Reconnected;

        /// <inheritdoc />
        public void AttachToRoom(Room room)
        {
            if (room == null) throw new ArgumentNullException(nameof(room));

            lock (_lock)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(RoomEventDispatcher));

                if (_room != null) DetachFromRoomInternal();

                _room = room;
                SubscribeToRoomEvents();
            }

            _logger?.Debug("Attached to room");
        }

        /// <inheritdoc />
        public void DetachFromRoom()
        {
            lock (_lock)
            {
                if (_room == null) return;

                DetachFromRoomInternal();
            }

            _logger?.Debug("Detached from room");
        }

        private void SubscribeToRoomEvents()
        {
            _room.ConnectionStateChanged += OnConnectionStateChanged;
            _room.ParticipantConnected += OnParticipantConnected;
            _room.ParticipantDisconnected += OnParticipantDisconnected;
            _room.TrackPublished += OnTrackPublished;
            _room.TrackUnpublished += OnTrackUnpublished;
            _room.TrackSubscribed += OnTrackSubscribed;
            _room.TrackUnsubscribed += OnTrackUnsubscribed;
            _room.DataReceived += OnDataReceived;
            _room.Disconnected += OnDisconnected;
            _room.Reconnecting += OnReconnecting;
            _room.Reconnected += OnReconnected;
        }

        private void UnsubscribeFromRoomEvents()
        {
            if (_room == null) return;

            _room.ConnectionStateChanged -= OnConnectionStateChanged;
            _room.ParticipantConnected -= OnParticipantConnected;
            _room.ParticipantDisconnected -= OnParticipantDisconnected;
            _room.TrackPublished -= OnTrackPublished;
            _room.TrackUnpublished -= OnTrackUnpublished;
            _room.TrackSubscribed -= OnTrackSubscribed;
            _room.TrackUnsubscribed -= OnTrackUnsubscribed;
            _room.DataReceived -= OnDataReceived;
            _room.Disconnected -= OnDisconnected;
            _room.Reconnecting -= OnReconnecting;
            _room.Reconnected -= OnReconnected;
        }

        private void DetachFromRoomInternal()
        {
            UnsubscribeFromRoomEvents();
            _room = null;
        }

        private void OnConnectionStateChanged(ConnectionState state) =>
            ConnectionStateChanged?.Invoke(state);

        private void OnParticipantConnected(Participant participant)
        {
            if (participant is RemoteParticipant remoteParticipant)
                ParticipantJoined?.Invoke(remoteParticipant);
        }

        private void OnParticipantDisconnected(Participant participant)
        {
            if (participant is RemoteParticipant remoteParticipant)
                ParticipantLeft?.Invoke(remoteParticipant);
        }

        private void OnTrackPublished(RemoteTrackPublication pub, RemoteParticipant participant) =>
            TrackPublished?.Invoke(pub, participant);

        private void OnTrackUnpublished(RemoteTrackPublication pub, RemoteParticipant participant) =>
            TrackUnpublished?.Invoke(pub, participant);

        private void
            OnTrackSubscribed(LKRemoteTrack track, RemoteTrackPublication pub, RemoteParticipant participant) =>
            TrackSubscribed?.Invoke(track, pub, participant);

        private void
            OnTrackUnsubscribed(LKRemoteTrack track, RemoteTrackPublication pub, RemoteParticipant participant) =>
            TrackUnsubscribed?.Invoke(track, pub, participant);

        private void OnDataReceived(byte[] data, Participant participant, DataPacketKind kind,
            string topic) => DataReceived?.Invoke(data, participant, kind, topic);

        private void OnDisconnected(Room room) => Disconnected?.Invoke(room);
        private void OnReconnecting(Room room) => Reconnecting?.Invoke();
        private void OnReconnected(Room room) => Reconnected?.Invoke();
    }
}
