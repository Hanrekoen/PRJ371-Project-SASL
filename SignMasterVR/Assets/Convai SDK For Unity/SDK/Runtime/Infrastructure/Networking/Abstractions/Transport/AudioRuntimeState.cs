namespace Convai.Infrastructure.Networking.Transport
{
    /// <summary>
    ///     Runtime audio state information.
    ///     Use for "is X currently available/active?" questions.
    /// </summary>
    public struct AudioRuntimeState
    {
        /// <summary>Whether audio playback is currently active.</summary>
        public bool IsAudioPlaybackActive { get; set; }

        /// <summary>Whether microphone is currently enabled and publishing.</summary>
        public bool IsMicrophoneEnabled { get; set; }

        /// <summary>Whether microphone is currently muted.</summary>
        public bool IsMicrophoneMuted { get; set; }

        /// <summary>Current microphone permission state.</summary>
        public PermissionState MicrophonePermission { get; set; }

        /// <summary>Whether user gesture is required before audio operations.</summary>
        public bool RequiresUserGesture { get; set; }

        /// <summary>
        ///     Creates a new AudioRuntimeState instance.
        /// </summary>
        public AudioRuntimeState(
            bool isAudioPlaybackActive = false,
            bool isMicrophoneEnabled = false,
            bool isMicrophoneMuted = false,
            PermissionState microphonePermission = PermissionState.Unknown,
            bool requiresUserGesture = false)
        {
            IsAudioPlaybackActive = isAudioPlaybackActive;
            IsMicrophoneEnabled = isMicrophoneEnabled;
            IsMicrophoneMuted = isMicrophoneMuted;
            MicrophonePermission = microphonePermission;
            RequiresUserGesture = requiresUserGesture;
        }
    }
}
