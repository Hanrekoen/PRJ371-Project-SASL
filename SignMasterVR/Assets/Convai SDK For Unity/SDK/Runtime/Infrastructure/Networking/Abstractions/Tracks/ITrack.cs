using System;
using Convai.Infrastructure.Networking.Transport;
using UnityEngine;

namespace Convai.Infrastructure.Networking
{
    /// <summary>
    ///     Base interface for all tracks (audio and video, local and remote).
    /// </summary>
    public interface ITrack
    {
        /// <summary>
        ///     Gets the unique session ID for this track.
        /// </summary>
        public string Sid { get; }

        /// <summary>
        ///     Gets the track name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        ///     Gets the kind of track (audio or video).
        /// </summary>
        public TrackKind Kind { get; }

        /// <summary>
        ///     Gets whether the track is currently muted.
        /// </summary>
        public bool IsMuted { get; }

        /// <summary>
        ///     Raised when the track's mute state changes.
        /// </summary>
        public event Action<bool> MuteChanged;
    }

    /// <summary>
    ///     Base interface for locally published tracks.
    /// </summary>
    public interface ILocalTrack : ITrack
    {
        /// <summary>
        ///     Gets whether this track is currently published.
        /// </summary>
        public bool IsPublished { get; }

        /// <summary>
        ///     Sets the mute state of the track.
        /// </summary>
        /// <param name="muted">True to mute, false to unmute.</param>
        public void SetMuted(bool muted);
    }

    /// <summary>
    ///     Base interface for remotely subscribed tracks.
    /// </summary>
    public interface IRemoteTrack : ITrack
    {
        /// <summary>
        ///     Gets the participant who published this track.
        /// </summary>
        public IRemoteParticipant Participant { get; }

        /// <summary>
        ///     Gets whether this track is currently subscribed.
        /// </summary>
        public bool IsSubscribed { get; }
    }

    /// <summary>
    ///     Internal control seam for toggling remote audio playback on an already-subscribed track.
    /// </summary>
    internal interface IRemoteAudioControlTrack
    {
        /// <summary>
        ///     Enables or disables remote audio playback for the track.
        /// </summary>
        public void SetRemoteAudioEnabled(bool enabled);
    }

    /// <summary>
    ///     Interface for local audio tracks.
    /// </summary>
    public interface ILocalAudioTrack : ILocalTrack
    {
        /// <summary>
        ///     Gets the audio source used for this track.
        /// </summary>
        public IAudioSource Source { get; }
    }

    /// <summary>
    ///     Interface for remote audio tracks.
    /// </summary>
    public interface IRemoteAudioTrack : IRemoteTrack
    {
        /// <summary>
        ///     Gets whether this track is currently attached to an AudioSource.
        /// </summary>
        public bool IsAttached { get; }

        /// <summary>
        ///     Creates an audio stream that can be used to route audio to a Unity AudioSource.
        /// </summary>
        /// <returns>An audio stream for playback.</returns>
        public IAudioStream CreateAudioStream();

        /// <summary>
        ///     Attaches this track's audio to a Unity AudioSource for playback.
        /// </summary>
        /// <param name="audioSource">The target AudioSource.</param>
        public void AttachToAudioSource(AudioSource audioSource);

        /// <summary>
        ///     Detaches this track from any attached AudioSource.
        /// </summary>
        public void Detach();
    }

    /// <summary>
    ///     Interface for local video tracks.
    /// </summary>
    public interface ILocalVideoTrack : ILocalTrack
    {
        /// <summary>
        ///     Gets the video source used for this track.
        /// </summary>
        public IVideoSource Source { get; }
    }

    /// <summary>
    ///     Interface for remote video tracks.
    /// </summary>
    public interface IRemoteVideoTrack : IRemoteTrack
    {
        /// <summary>
        ///     Gets whether this track is currently attached.
        /// </summary>
        public bool IsAttached { get; }

        /// <summary>
        ///     Gets the video dimensions if available.
        /// </summary>
        public (int width, int height)? Dimensions { get; }

        /// <summary>
        ///     Attaches this track's video to a Unity RenderTexture for display.
        /// </summary>
        /// <param name="target">The target RenderTexture.</param>
        public void AttachToRenderTexture(RenderTexture target);

        /// <summary>
        ///     Detaches this track from any attached render target.
        /// </summary>
        public void Detach();
    }
}
