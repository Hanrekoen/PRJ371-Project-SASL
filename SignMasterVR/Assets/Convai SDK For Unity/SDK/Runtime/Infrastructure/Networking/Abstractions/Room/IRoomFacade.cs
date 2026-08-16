using System;
using System.Collections.Generic;
using Convai.Infrastructure.Networking.Transport;

namespace Convai.Infrastructure.Networking
{
    /// <summary>
    ///     Provides a platform-agnostic facade over a real-time communication room.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Key responsibilities:
    ///         <list type="bullet">
    ///             <item>
    ///                 <description>Expose room state and metadata</description>
    ///             </item>
    ///             <item>
    ///                 <description>Provide access to local and remote participants</description>
    ///             </item>
    ///             <item>
    ///                 <description>Emit events for participant and track changes</description>
    ///             </item>
    ///         </list>
    ///     </para>
    /// </remarks>
    public interface IRoomFacade
    {
        #region Properties

        /// <summary>
        ///     Gets the room's unique session ID.
        /// </summary>
        public string Sid { get; }

        /// <summary>
        ///     Gets the room name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        ///     Gets the current room connection state.
        /// </summary>
        public RoomState State { get; }

        /// <summary>
        ///     Gets whether the room is currently connected.
        /// </summary>
        public bool IsConnected { get; }

        /// <summary>
        ///     Gets the local participant.
        /// </summary>
        public ILocalParticipant LocalParticipant { get; }

        /// <summary>
        ///     Gets the list of remote participants currently in the room.
        /// </summary>
        public IReadOnlyList<IRemoteParticipant> RemoteParticipants { get; }

        /// <summary>
        ///     Gets all participants (local + remote).
        /// </summary>
        public IEnumerable<IParticipant> AllParticipants { get; }

        /// <summary>
        ///     Gets the number of remote participants.
        /// </summary>
        public int RemoteParticipantCount { get; }

        #endregion

        #region Participant Lookup

        /// <summary>
        ///     Gets a remote participant by their session ID.
        /// </summary>
        /// <param name="sid">The participant's session ID.</param>
        /// <returns>The participant, or null if not found.</returns>
        public IRemoteParticipant GetParticipantBySid(string sid);

        /// <summary>
        ///     Gets a remote participant by their identity.
        /// </summary>
        /// <param name="identity">The participant's identity.</param>
        /// <returns>The participant, or null if not found.</returns>
        public IRemoteParticipant GetParticipantByIdentity(string identity);

        /// <summary>
        ///     Attempts to get a remote participant by their session ID.
        /// </summary>
        /// <param name="sid">The participant's session ID.</param>
        /// <param name="participant">The found participant, or null.</param>
        /// <returns>True if the participant was found.</returns>
        public bool TryGetParticipantBySid(string sid, out IRemoteParticipant participant);

        /// <summary>
        ///     Attempts to get a remote participant by their identity.
        /// </summary>
        /// <param name="identity">The participant's identity.</param>
        /// <param name="participant">The found participant, or null.</param>
        /// <returns>True if the participant was found.</returns>
        public bool TryGetParticipantByIdentity(string identity, out IRemoteParticipant participant);

        #endregion

        #region Participant Events

        /// <summary>
        ///     Raised when a remote participant joins the room.
        /// </summary>
        public event Action<IRemoteParticipant> ParticipantJoined;

        /// <summary>
        ///     Raised when a remote participant leaves the room.
        /// </summary>
        public event Action<IRemoteParticipant> ParticipantLeft;

        /// <summary>
        ///     Raised when a participant's metadata is updated.
        /// </summary>
        public event Action<IParticipant, ParticipantMetadata> ParticipantMetadataUpdated;

        #endregion

        #region Track Events

        /// <summary>
        ///     Raised when a remote audio track is subscribed.
        /// </summary>
        public event Action<IRemoteAudioTrack, IRemoteParticipant> AudioTrackSubscribed;

        /// <summary>
        ///     Raised when a remote audio track subscription ends.
        /// </summary>
        public event Action<IRemoteAudioTrack, IRemoteParticipant> AudioTrackUnsubscribed;

        /// <summary>
        ///     Raised when a remote video track is subscribed.
        /// </summary>
        public event Action<IRemoteVideoTrack, IRemoteParticipant> VideoTrackSubscribed;

        /// <summary>
        ///     Raised when a remote video track subscription ends.
        /// </summary>
        public event Action<IRemoteVideoTrack, IRemoteParticipant> VideoTrackUnsubscribed;

        /// <summary>
        ///     Raised when any track is subscribed (audio or video).
        /// </summary>
        public event Action<TrackSubscriptionEventArgs> TrackSubscribed;

        /// <summary>
        ///     Raised when any track subscription ends.
        /// </summary>
        public event Action<TrackSubscriptionEventArgs> TrackUnsubscribed;

        #endregion

        #region Room Events

        /// <summary>
        ///     Raised when the room state changes.
        /// </summary>
        public event Action<RoomState> StateChanged;

        /// <summary>
        ///     Raised when the room is reconnecting.
        /// </summary>
        public event Action Reconnecting;

        /// <summary>
        ///     Raised when the room has reconnected.
        /// </summary>
        public event Action Reconnected;

        /// <summary>
        ///     Raised when the room is disconnected.
        /// </summary>
        public event Action<DisconnectReason> Disconnected;

        #endregion
    }
}
