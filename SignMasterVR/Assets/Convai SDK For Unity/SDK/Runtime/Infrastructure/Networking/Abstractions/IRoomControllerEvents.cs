using System;

namespace Convai.Infrastructure.Networking
{
    /// <summary>
    ///     Focused interface for room controller event subscriptions.
    ///     Extracted from <see cref="IConvaiRoomController" /> so consumers that only
    ///     wire up event handlers (e.g. <c>RoomControllerEventBinder</c>) can depend
    ///     on this narrow contract instead of the full controller surface.
    /// </summary>
    public interface IRoomControllerEvents
    {
        /// <summary>
        ///     Raised when room connection succeeds.
        /// </summary>
        public event Action OnRoomConnectionSuccessful;

        /// <summary>
        ///     Raised when room connection fails.
        /// </summary>
        public event Action OnRoomConnectionFailed;

        /// <summary>
        ///     Raised when microphone mute state changes.
        ///     Parameter: new mute state (true = muted).
        /// </summary>
        public event Action<bool> OnMicMuteChanged;

        /// <summary>
        ///     Raised when the room is reconnecting.
        /// </summary>
        public event Action OnRoomReconnecting;

        /// <summary>
        ///     Raised when the room has reconnected.
        /// </summary>
        public event Action OnRoomReconnected;

        /// <summary>
        ///     Raised when the room is disconnected unexpectedly and controller-owned
        ///     teardown/reset has completed.
        /// </summary>
        public event Action OnUnexpectedRoomDisconnected;

        /// <summary>
        ///     Raised when a remote audio track is subscribed.
        ///     Parameters: (audioTrack, participantSid, characterId).
        /// </summary>
        public event Action<IRemoteAudioTrack, string, string> OnRemoteAudioTrackSubscribed;

        /// <summary>
        ///     Raised when a remote audio track is unsubscribed.
        ///     Parameters: (participantSid, characterId).
        /// </summary>
        public event Action<string, string> OnRemoteAudioTrackUnsubscribed;
    }
}
