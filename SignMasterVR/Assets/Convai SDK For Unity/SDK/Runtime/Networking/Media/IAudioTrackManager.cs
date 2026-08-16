using System;
using System.Threading.Tasks;
using Convai.Infrastructure.Networking;

namespace Convai.Runtime.Networking.Media
{
    /// <summary>
    ///     Interface for managing audio track operations in Convai room connections.
    ///     Handles microphone publishing, track subscription, and Character audio routing.
    ///     Uses platform-agnostic abstractions for cross-platform compatibility.
    /// </summary>
    internal interface IAudioTrackManager : IDisposable
    {
        /// <summary>
        ///     Gets a value indicating whether the microphone is currently muted.
        /// </summary>
        public bool IsMicMuted { get; }

        /// <summary>
        ///     Gets a value indicating whether the microphone is currently publishing.
        /// </summary>
        public bool IsPublishing { get; }

        /// <summary>
        ///     Raised when the microphone mute state changes.
        /// </summary>
        public event Action<bool> OnMicMuteChanged;

        /// <summary>
        ///     Raised when an audio track is subscribed from a remote participant.
        /// </summary>
        public event Action<IRemoteAudioTrack, IRemoteParticipant> OnAudioTrackSubscribed;

        /// <summary>
        ///     Raised when an audio track is unsubscribed from a remote participant.
        /// </summary>
        public event Action<IRemoteAudioTrack, IRemoteParticipant> OnAudioTrackUnsubscribed;

        /// <summary>
        ///     Publishes a microphone audio track to the room.
        /// </summary>
        /// <param name="microphoneSource">The microphone source to publish.</param>
        /// <param name="options">Audio publish options.</param>
        /// <returns>A task that completes with true if publishing succeeded; otherwise, false.</returns>
        public Task<bool> PublishMicrophoneAsync(IMicrophoneSource microphoneSource, AudioPublishOptions options);

        /// <summary>
        ///     Unpublishes the current microphone audio track.
        /// </summary>
        /// <returns>A task that completes when unpublishing is done.</returns>
        public Task UnpublishMicrophoneAsync();

        /// <summary>
        ///     Sets the microphone mute state.
        /// </summary>
        /// <param name="muted">True to mute the microphone; false to unmute.</param>
        public void SetMicMuted(bool muted);

        /// <summary>
        ///     Toggles the microphone mute state.
        /// </summary>
        public void ToggleMicMute();

        /// <summary>
        ///     Clears internal track and microphone references without attempting to unpublish.
        ///     Also clears all remote audio streams.
        ///     Call this when the room is being reset to avoid stale reference warnings.
        /// </summary>
        public void ClearState();

        #region Remote Player Management

        /// <summary>
        ///     Registers a remote player for audio routing.
        /// </summary>
        /// <param name="participantId">The participant identifier.</param>
        /// <param name="displayName">The display name of the remote player.</param>
        public void RegisterRemotePlayer(string participantId, string displayName);

        /// <summary>
        ///     Unregisters a remote player from audio routing.
        /// </summary>
        /// <param name="participantId">The participant identifier.</param>
        public void UnregisterRemotePlayer(string participantId);

        /// <summary>
        ///     Subscribes to audio from a remote player.
        /// </summary>
        /// <param name="participantId">The participant identifier.</param>
        public void SubscribeToPlayerAudio(string participantId);

        /// <summary>
        ///     Unsubscribes from audio from a remote player.
        /// </summary>
        /// <param name="participantId">The participant identifier.</param>
        public void UnsubscribeFromPlayerAudio(string participantId);

        #endregion

        #region Character Audio Management

        /// <summary>
        ///     Sets the mute state for a Character's audio output.
        /// </summary>
        /// <param name="characterId">The Character identifier.</param>
        /// <param name="muted">True to mute; false to unmute.</param>
        /// <returns>True if the Character was found and mute state was set; otherwise, false.</returns>
        public bool SetCharacterAudioMuted(string characterId, bool muted);

        /// <summary>
        ///     Gets the mute state for a Character's audio output.
        /// </summary>
        /// <param name="characterId">The Character identifier.</param>
        /// <returns>True if the Character's audio is muted; false otherwise.</returns>
        public bool IsCharacterAudioMuted(string characterId);

        /// <summary>
        ///     Handles the event when a remote audio track is subscribed for a Character participant.
        /// </summary>
        /// <param name="audioTrack">The remote audio track that was subscribed.</param>
        /// <param name="participantSid">The unique session identifier for the participant.</param>
        /// <param name="participantIdentity">The identity string of the participant.</param>
        public void HandleRemoteAudioTrackSubscribed(IRemoteAudioTrack audioTrack, string participantSid,
            string participantIdentity);

        /// <summary>
        ///     Handles the event when a remote audio track is unsubscribed for a given participant.
        /// </summary>
        /// <param name="participantSid">The unique identifier of the participant.</param>
        public void HandleRemoteAudioTrackUnsubscribed(string participantSid);

        /// <summary>
        ///     Disposes and clears all remote audio streams managed by this instance.
        /// </summary>
        public void ClearRemoteAudio();

        #endregion
    }
}
