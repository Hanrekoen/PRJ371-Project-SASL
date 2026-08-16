using System;
using Convai.Infrastructure.Networking;

namespace Convai.Runtime.Adapters.Networking
{
    internal sealed class RoomControllerEventBinder : IDisposable
    {
        private readonly Action<bool> _onMicMuteChanged;
        private readonly Action<IRemoteAudioTrack, string, string> _onRemoteAudioTrackSubscribed;
        private readonly Action<string, string> _onRemoteAudioTrackUnsubscribed;
        private readonly Action _onUnexpectedRoomDisconnected;
        private IRoomControllerEvents _roomControllerEvents;

        public RoomControllerEventBinder(
            Action<bool> onMicMuteChanged,
            Action onUnexpectedRoomDisconnected,
            Action<IRemoteAudioTrack, string, string> onRemoteAudioTrackSubscribed,
            Action<string, string> onRemoteAudioTrackUnsubscribed)
        {
            _onMicMuteChanged = onMicMuteChanged ?? throw new ArgumentNullException(nameof(onMicMuteChanged));
            _onUnexpectedRoomDisconnected = onUnexpectedRoomDisconnected ??
                                            throw new ArgumentNullException(nameof(onUnexpectedRoomDisconnected));
            _onRemoteAudioTrackSubscribed = onRemoteAudioTrackSubscribed ??
                                            throw new ArgumentNullException(nameof(onRemoteAudioTrackSubscribed));
            _onRemoteAudioTrackUnsubscribed = onRemoteAudioTrackUnsubscribed ??
                                              throw new ArgumentNullException(nameof(onRemoteAudioTrackUnsubscribed));
        }

        public void Dispose() => Detach();

        public void Attach(IRoomControllerEvents roomControllerEvents)
        {
            if (ReferenceEquals(_roomControllerEvents, roomControllerEvents)) return;

            Detach();
            _roomControllerEvents = roomControllerEvents;
            if (_roomControllerEvents == null) return;

            _roomControllerEvents.OnMicMuteChanged += _onMicMuteChanged;
            _roomControllerEvents.OnUnexpectedRoomDisconnected += _onUnexpectedRoomDisconnected;
            _roomControllerEvents.OnRemoteAudioTrackSubscribed += _onRemoteAudioTrackSubscribed;
            _roomControllerEvents.OnRemoteAudioTrackUnsubscribed += _onRemoteAudioTrackUnsubscribed;
        }

        public void Detach()
        {
            if (_roomControllerEvents == null) return;

            _roomControllerEvents.OnMicMuteChanged -= _onMicMuteChanged;
            _roomControllerEvents.OnUnexpectedRoomDisconnected -= _onUnexpectedRoomDisconnected;
            _roomControllerEvents.OnRemoteAudioTrackSubscribed -= _onRemoteAudioTrackSubscribed;
            _roomControllerEvents.OnRemoteAudioTrackUnsubscribed -= _onRemoteAudioTrackUnsubscribed;
            _roomControllerEvents = null;
        }
    }
}
