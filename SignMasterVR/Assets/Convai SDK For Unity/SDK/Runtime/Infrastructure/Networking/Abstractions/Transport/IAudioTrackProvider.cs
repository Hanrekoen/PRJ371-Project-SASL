using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Convai.Infrastructure.Networking.Transport
{
    /// <summary>
    ///     Platform-agnostic abstraction for audio track management.
    ///     Handles microphone capture and remote audio playback.
    ///     This interface is internal - consumers should use <see cref="IRealtimeTransport" />
    ///     for audio operations. Advanced integrators can access this via DI for custom audio handling.
    /// </summary>
    /// <remarks>
    ///     Platform differences handled by implementations:
    ///     <list type="bullet">
    ///         <item>
    ///             <description>Native: MicrophoneSource, LocalAudioTrack, AudioStream to Unity AudioSource</description>
    ///         </item>
    ///         <item>
    ///             <description>WebGL: Browser getUserMedia, SetMicrophoneEnabled, track.Attach() to browser audio element</description>
    ///         </item>
    ///     </list>
    /// </remarks>
    internal interface IAudioTrackProvider : IDisposable
    {
        #region State Properties

        /// <summary>
        ///     Gets whether microphone capture is currently enabled and publishing audio.
        /// </summary>
        public bool IsMicrophoneEnabled { get; }

        /// <summary>
        ///     Gets whether microphone is muted (track enabled but audio silenced).
        /// </summary>
        public bool IsMicrophoneMuted { get; }

        /// <summary>
        ///     Gets whether audio playback capability is activated.
        ///     On WebGL, this requires a user gesture to start the audio context.
        ///     On native platforms, this is always true.
        /// </summary>
        public bool IsAudioPlaybackActive { get; }

        /// <summary>
        ///     Gets the current microphone permission state.
        /// </summary>
        public PermissionState MicrophonePermissionState { get; }

        /// <summary>
        ///     Gets the current runtime audio state.
        /// </summary>
        public AudioRuntimeState RuntimeState { get; }

        #endregion

        #region Events

        /// <summary>
        ///     Raised when microphone enabled state changes.
        ///     Parameter: true if enabled, false if disabled.
        /// </summary>
        public event Action<bool> MicrophoneEnabledChanged;

        /// <summary>
        ///     Raised when microphone mute state changes.
        ///     Parameter: true if muted, false if unmuted.
        /// </summary>
        public event Action<bool> MicrophoneMuteChanged;

        /// <summary>
        ///     Raised when audio playback activation state changes.
        ///     Parameter: true if active, false if inactive.
        /// </summary>
        public event Action<bool> AudioPlaybackStateChanged;

        /// <summary>
        ///     Raised when a remote audio track is subscribed and available for playback.
        /// </summary>
        public event Action<RemoteAudioTrackInfo> RemoteAudioTrackSubscribed;

        /// <summary>
        ///     Raised when a remote audio track is unsubscribed.
        ///     Parameter: participant ID whose track was unsubscribed.
        /// </summary>
        public event Action<string> RemoteAudioTrackUnsubscribed;

        /// <summary>
        ///     Raised when microphone permission state changes.
        /// </summary>
        public event Action<PermissionState> MicrophonePermissionChanged;

        #endregion

        #region Microphone Control

        /// <summary>
        ///     Enables microphone capture and publishes audio track to the room.
        /// </summary>
        /// <remarks>
        ///     Platform behavior:
        ///     <list type="bullet">
        ///         <item>
        ///             <description>Native: Creates MicrophoneSource, publishes LocalAudioTrack</description>
        ///         </item>
        ///         <item>
        ///             <description>WebGL: Requests browser permission, calls SetMicrophoneEnabled(true)</description>
        ///         </item>
        ///     </list>
        ///     On WebGL, this must be called from a user gesture context (button click).
        /// </remarks>
        /// <param name="microphoneDeviceIndex">Microphone device index (native only, ignored on WebGL).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>True if microphone was successfully enabled; false otherwise.</returns>
        public Task<bool> EnableMicrophoneAsync(int microphoneDeviceIndex = 0, CancellationToken ct = default);

        /// <summary>
        ///     Disables microphone capture and unpublishes the audio track.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        public Task DisableMicrophoneAsync(CancellationToken ct = default);

        /// <summary>
        ///     Sets the microphone mute state without unpublishing the track.
        ///     When muted, the track continues to publish but sends silence.
        /// </summary>
        /// <param name="muted">True to mute; false to unmute.</param>
        public void SetMicrophoneMuted(bool muted);

        /// <summary>
        ///     Checks if microphone can be enabled based on current permission and gesture state.
        /// </summary>
        /// <returns>True if EnableMicrophoneAsync is likely to succeed.</returns>
        public bool CanEnableMicrophone();

        #endregion

        #region Audio Playback Control

        /// <summary>
        ///     Activates audio playback capability.
        /// </summary>
        /// <remarks>
        ///     Platform behavior:
        ///     <list type="bullet">
        ///         <item>
        ///             <description>Native: No-op (audio is always active)</description>
        ///         </item>
        ///         <item>
        ///             <description>WebGL: Starts browser audio context (requires user gesture)</description>
        ///         </item>
        ///     </list>
        /// </remarks>
        public void ActivateAudioPlayback();

        /// <summary>
        ///     Enables playback for a specific remote participant's audio track.
        /// </summary>
        /// <param name="participantId">Remote participant's ID.</param>
        /// <param name="targetAudioSource">
        ///     Unity AudioSource to route audio to (native only).
        ///     On WebGL, audio plays via browser audio element regardless of this parameter.
        /// </param>
        public void EnableRemoteAudio(string participantId, AudioSource targetAudioSource = null);

        /// <summary>
        ///     Disables playback for a specific remote participant's audio track.
        /// </summary>
        /// <param name="participantId">Remote participant's ID.</param>
        public void DisableRemoteAudio(string participantId);

        /// <summary>
        ///     Sets the volume for a remote participant's audio.
        /// </summary>
        /// <param name="participantId">Remote participant's ID.</param>
        /// <param name="volume">Volume level (0.0 to 1.0).</param>
        public void SetRemoteAudioVolume(string participantId, float volume);

        /// <summary>
        ///     Checks if audio playback can be activated.
        ///     On WebGL, returns false if called outside user gesture context.
        /// </summary>
        public bool CanActivateAudioPlayback();

        #endregion
    }
}
