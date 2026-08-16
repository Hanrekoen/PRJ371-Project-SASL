using System;
using System.Threading;
using Convai.Runtime.Core.Async;
using Convai.Runtime.Core.Coordinators;
using Convai.Infrastructure.Networking;

namespace Convai.Runtime.Room
{
    internal interface IConvaiRoomAudioTimelineService
    {
        bool TryGetCharacterAudioTimeline(string characterId, out AudioTimelineSnapshot snapshot);
    }

    internal interface IConvaiRoomAudioMediaTimelineService
    {
        bool TryGetCharacterAudioMediaTimeline(string characterId, out AudioMediaTimelineSnapshot snapshot);
    }

    /// <summary>
    ///     Provides Unity-friendly microphone and Character audio controls for the Convai room.
    /// </summary>
    public interface IConvaiRoomAudioService
    {
        /// <summary>
        ///     Indicates whether the microphone is currently muted.
        /// </summary>
        public bool IsMicMuted { get; }

        /// <summary>
        ///     Whether the current platform requires a user gesture before audio playback can start (e.g., WebGL).
        /// </summary>
        public bool RequiresUserGestureForAudio { get; }

        /// <summary>
        ///     Whether audio playback is currently active.
        /// </summary>
        public bool IsAudioPlaybackActive { get; }

        /// <summary>
        ///     Whether audio playback can be enabled (e.g., after a user gesture on WebGL).
        /// </summary>
        public bool CanEnableAudioPlayback { get; }

        /// <summary>
        ///     Raised whenever the local microphone mute state changes.
        /// </summary>
        public event Action<bool> MicMuteChanged;

        /// <summary>
        ///     Starts microphone capture and publishes the audio track.
        /// </summary>
        /// <param name="microphoneIndex">Zero-based microphone device index.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        public IConvaiOperation<Unit> StartListeningAsync(int microphoneIndex = 0,
            CancellationToken cancellationToken = default);

        /// <summary>
        ///     Stops microphone capture and unpublishes the audio track.
        /// </summary>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        public IConvaiOperation<Unit> StopListeningAsync(CancellationToken cancellationToken = default);

        /// <summary>
        ///     Sets the microphone mute state for the local participant.
        /// </summary>
        /// <param name="muted">True to mute; false to unmute.</param>
        public void SetMicMuted(bool muted);

        /// <summary>
        ///     Sets the mute state for the supplied Character identifier.
        /// </summary>
        /// <param name="characterId">Convai character identifier.</param>
        /// <param name="muted">True to mute; false to unmute.</param>
        /// <returns>True when the operation succeeds.</returns>
        public bool SetCharacterMuted(string characterId, bool muted);

        /// <summary>
        ///     Checks whether the supplied Character identifier is muted.
        /// </summary>
        /// <param name="characterId">Convai character identifier.</param>
        /// <returns>True when the Character is muted; otherwise false.</returns>
        public bool IsCharacterMuted(string characterId);

        /// <summary>
        ///     Raised whenever the remote audio enabled state changes for a character.
        ///     Parameters: (characterId, enabled)
        /// </summary>
        public event Action<string, bool> RemoteAudioEnabledChanged;

        /// <summary>
        ///     Enables or disables remote audio playback for the specified character.
        ///     When disabled, the character's audio track is unsubscribed (no audio packets received).
        ///     When enabled, the character's audio track is subscribed and routed to the AudioSource.
        /// </summary>
        /// <param name="characterId">Convai character identifier.</param>
        /// <param name="enabled">True to enable audio playback; false to disable.</param>
        /// <returns>True when the operation succeeds; otherwise false.</returns>
        public bool SetRemoteAudioEnabled(string characterId, bool enabled);

        /// <summary>
        ///     Checks whether remote audio playback is enabled for the specified character.
        /// </summary>
        /// <param name="characterId">Convai character identifier.</param>
        /// <returns>True when remote audio is enabled; otherwise false.</returns>
        public bool IsRemoteAudioEnabled(string characterId);

        /// <summary>
        ///     Enables audio playback after a user gesture. Required on platforms like WebGL.
        /// </summary>
        public void EnableAudioPlayback();

        /// <summary>
        ///     Reads the measured audio playhead for a character's remote stream: seconds of source
        ///     audio actually rendered to the output device since the current playback signal started.
        ///     Freezes on underrun; accounts for drift-correction skips. Returns false when the
        ///     platform stream does not expose a playhead (callers should fall back to wall clock).
        /// </summary>
        public bool TryGetCharacterAudioPlayhead(string characterId, out double playedSeconds);
    }
}
