using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Convai.Infrastructure.Networking
{
    /// <summary>
    ///     Base interface for all participants (local and remote).
    /// </summary>
    public interface IParticipant
    {
        /// <summary>
        ///     Gets the unique session ID for this participant.
        /// </summary>
        public string Sid { get; }

        /// <summary>
        ///     Gets the identity string for this participant.
        /// </summary>
        public string Identity { get; }

        /// <summary>
        ///     Gets the participant's display name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        ///     Gets the participant's metadata.
        /// </summary>
        public ParticipantMetadata Metadata { get; }

        /// <summary>
        ///     Gets whether this participant represents a Convai character/agent.
        /// </summary>
        public bool IsAgent { get; }

        /// <summary>
        ///     Raised when metadata is updated.
        /// </summary>
        public event Action<ParticipantMetadata> MetadataUpdated;
    }

    /// <summary>
    ///     Represents the local participant (the current user).
    /// </summary>
    public interface ILocalParticipant : IParticipant
    {
        /// <summary>
        ///     Gets the list of locally published tracks.
        /// </summary>
        public IReadOnlyList<ILocalTrack> LocalTracks { get; }

        /// <summary>
        ///     Publishes an audio track to the room.
        /// </summary>
        /// <param name="source">The audio source to publish.</param>
        /// <param name="options">Publishing options.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The published local audio track.</returns>
        public Task<ILocalAudioTrack> PublishAudioTrackAsync(
            IAudioSource source,
            AudioPublishOptions options = default,
            CancellationToken ct = default);

        /// <summary>
        ///     Publishes a video track to the room.
        /// </summary>
        /// <param name="source">The video source to publish.</param>
        /// <param name="options">Publishing options.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The published local video track.</returns>
        public Task<ILocalVideoTrack> PublishVideoTrackAsync(
            IVideoSource source,
            VideoPublishOptions options = default,
            CancellationToken ct = default);

        /// <summary>
        ///     Unpublishes a track from the room.
        /// </summary>
        /// <param name="track">The track to unpublish.</param>
        /// <param name="ct">Cancellation token.</param>
        public Task UnpublishTrackAsync(ILocalTrack track, CancellationToken ct = default);

        /// <summary>
        ///     Sets the mute state for all audio tracks.
        /// </summary>
        /// <param name="muted">True to mute, false to unmute.</param>
        public void SetAudioMuted(bool muted);

        /// <summary>
        ///     Sets the mute state for all video tracks.
        /// </summary>
        /// <param name="muted">True to mute, false to unmute.</param>
        public void SetVideoMuted(bool muted);

        /// <summary>
        ///     Raised when a local track is published.
        /// </summary>
        public event Action<ILocalTrack> TrackPublished;

        /// <summary>
        ///     Raised when a local track is unpublished.
        /// </summary>
        public event Action<ILocalTrack> TrackUnpublished;
    }

    /// <summary>
    ///     Represents a remote participant in the room.
    /// </summary>
    public interface IRemoteParticipant : IParticipant
    {
        /// <summary>
        ///     Gets the list of track publications from this participant.
        /// </summary>
        public IReadOnlyList<TrackPublicationInfo> TrackPublications { get; }

        /// <summary>
        ///     Gets the list of subscribed remote tracks.
        /// </summary>
        public IReadOnlyList<IRemoteTrack> SubscribedTracks { get; }

        /// <summary>
        ///     Gets audio tracks from this participant.
        /// </summary>
        public IEnumerable<IRemoteAudioTrack> AudioTracks { get; }

        /// <summary>
        ///     Gets video tracks from this participant.
        /// </summary>
        public IEnumerable<IRemoteVideoTrack> VideoTracks { get; }

        /// <summary>
        ///     Raised when a track is subscribed from this participant.
        /// </summary>
        public event Action<IRemoteTrack, TrackPublicationInfo> TrackSubscribed;

        /// <summary>
        ///     Raised when a track subscription ends.
        /// </summary>
        public event Action<IRemoteTrack, TrackPublicationInfo> TrackUnsubscribed;

        /// <summary>
        ///     Raised when a track's mute state changes.
        /// </summary>
        public event Action<TrackPublicationInfo, bool> TrackMuteChanged;
    }
}
