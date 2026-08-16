using System;
using System.Threading;
using Convai.Runtime.Core.Async;
using Convai.Runtime.Core.Coordinators;
using Convai.Runtime.Room;

namespace Convai.Runtime.Facades
{
    /// <summary>
    ///     Convenience facade that exposes common room audio controls.
    ///     Accessed through ConvaiManager.Audio.
    /// </summary>
    public sealed class ConvaiAudio
    {
        private readonly IConvaiRoomAudioService _audioService;

        internal ConvaiAudio(IConvaiRoomAudioService audioService)
        {
            _audioService = audioService ?? throw new ArgumentNullException(nameof(audioService));
        }

        /// <summary>Whether the microphone is currently muted.</summary>
        public bool IsMicMuted => _audioService.IsMicMuted;

        /// <summary>Whether the platform requires a user gesture before audio playback can start.</summary>
        public bool RequiresUserGesture => _audioService.RequiresUserGestureForAudio;

        /// <summary>Whether audio playback is currently active.</summary>
        public bool IsAudioPlaybackActive => _audioService.IsAudioPlaybackActive;

        /// <summary>Whether audio playback can currently be enabled.</summary>
        public bool CanEnableAudioPlayback => _audioService.CanEnableAudioPlayback;

        /// <summary>Raised whenever the local microphone mute state changes.</summary>
        public event Action<bool> OnMicMuteChanged
        {
            add => _audioService.MicMuteChanged += value;
            remove => _audioService.MicMuteChanged -= value;
        }

        /// <summary>Raised whenever remote audio enabled state changes for a character. Parameters: (characterId, enabled).</summary>
        public event Action<string, bool> OnRemoteAudioEnabledChanged
        {
            add => _audioService.RemoteAudioEnabledChanged += value;
            remove => _audioService.RemoteAudioEnabledChanged -= value;
        }

        /// <summary>Sets the local microphone mute state.</summary>
        public void SetMicMuted(bool muted) => _audioService.SetMicMuted(muted);

        /// <summary>Toggles the local microphone mute state.</summary>
        public bool ToggleMicMuted()
        {
            bool newState = !_audioService.IsMicMuted;
            _audioService.SetMicMuted(newState);
            return newState;
        }

        /// <summary>Starts microphone capture and publishes an audio track.</summary>
        public IConvaiOperation<Unit> StartListeningAsync(int microphoneIndex = 0,
            CancellationToken cancellationToken = default) =>
            _audioService.StartListeningAsync(microphoneIndex, cancellationToken);

        /// <summary>Stops microphone capture and unpublishes the audio track.</summary>
        public IConvaiOperation<Unit> StopListeningAsync(CancellationToken cancellationToken = default) =>
            _audioService.StopListeningAsync(cancellationToken);

        /// <summary>Sets per-character playback mute state.</summary>
        public bool SetCharacterMuted(string characterId, bool muted) =>
            _audioService.SetCharacterMuted(characterId, muted);

        /// <summary>Convenience method to mute character playback.</summary>
        public bool MuteCharacter(string characterId) => _audioService.SetCharacterMuted(characterId, true);

        /// <summary>Convenience method to unmute character playback.</summary>
        public bool UnmuteCharacter(string characterId) => _audioService.SetCharacterMuted(characterId, false);

        /// <summary>Checks whether a character is muted.</summary>
        public bool IsCharacterMuted(string characterId) => _audioService.IsCharacterMuted(characterId);

        /// <summary>Enables or disables remote audio playback for a specific character.</summary>
        public bool SetRemoteAudioEnabled(string characterId, bool enabled) =>
            _audioService.SetRemoteAudioEnabled(characterId, enabled);

        /// <summary>Checks whether remote audio playback is enabled for a specific character.</summary>
        public bool IsRemoteAudioEnabled(string characterId) => _audioService.IsRemoteAudioEnabled(characterId);

        /// <summary>Enables audio playback after a required user gesture (WebGL).</summary>
        public void EnableAudioPlayback() => _audioService.EnableAudioPlayback();
    }
}
