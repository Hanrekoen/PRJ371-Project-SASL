namespace Convai.Infrastructure.Networking.Transport
{
    /// <summary>
    ///     Static platform capabilities for a transport implementation.
    ///     Use for "can this platform ever support X?" questions.
    /// </summary>
    public struct TransportCapabilities
    {
        /// <summary>Current platform type.</summary>
        public TransportPlatform Platform { get; set; }

        /// <summary>Whether spatial/3D audio positioning is supported.</summary>
        public bool SupportsSpatialAudio { get; set; }

        /// <summary>Whether video tracks are supported.</summary>
        public bool SupportsVideo { get; set; }

        /// <summary>Whether screen sharing is supported.</summary>
        public bool SupportsScreenShare { get; set; }

        /// <summary>Whether audio routes to Unity AudioSource (false = browser audio element).</summary>
        public bool SupportsUnityAudioSource { get; set; }

        /// <summary>Whether user gesture is required for audio playback/microphone.</summary>
        public bool RequiresUserGestureForAudio { get; set; }

        /// <summary>Whether microphone device selection is supported.</summary>
        public bool SupportsMicrophoneSelection { get; set; }

        /// <summary>
        ///     Creates capabilities for native desktop/mobile platforms.
        /// </summary>
        public static TransportCapabilities Native(bool isMobile = false)
        {
            return new TransportCapabilities
            {
                Platform = isMobile ? TransportPlatform.Mobile : TransportPlatform.Desktop,
                SupportsSpatialAudio = true,
                SupportsVideo = true,
                SupportsScreenShare = !isMobile,
                SupportsUnityAudioSource = true,
                RequiresUserGestureForAudio = false,
                SupportsMicrophoneSelection = true
            };
        }

        /// <summary>
        ///     Creates capabilities for WebGL platform.
        /// </summary>
        public static TransportCapabilities WebGL()
        {
            return new TransportCapabilities
            {
                Platform = TransportPlatform.WebGL,
                SupportsSpatialAudio = false,
                SupportsVideo = true,
                SupportsScreenShare = false,
                SupportsUnityAudioSource = false,
                RequiresUserGestureForAudio = true,
                SupportsMicrophoneSelection = false
            };
        }
    }
}
