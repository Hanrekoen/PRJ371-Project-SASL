using System;

namespace Convai.Runtime.Core.Configuration
{
    /// <summary>
    ///     Mutable runtime preferences that can be changed during gameplay.
    ///     Changes fire the <see cref="PreferenceChanged" /> event.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Unlike <see cref="ConvaiBootstrapConfigSnapshot" /> which is immutable after startup,
    ///         runtime preferences can be modified by the user during gameplay (e.g., through settings UI).
    ///     </para>
    ///     <para>
    ///         <b>Persistence</b>: Implementations should persist preferences across sessions
    ///         (e.g., using PlayerPrefs or <see cref="IPersistenceProvider" />).
    ///     </para>
    ///     <para>
    ///         <b>Thread Safety</b>: Implementations must ensure thread-safe property access.
    ///         Events are raised on the main thread.
    ///     </para>
    /// </remarks>
    public interface IRuntimePreferences
    {
        #region Microphone Settings

        /// <summary>
        ///     Preferred microphone device ID. Null or empty means system default.
        /// </summary>
        public string PreferredMicrophoneDeviceId { get; set; }

        #endregion

        #region Notification Settings

        /// <summary>
        ///     Whether in-game notifications are enabled.
        /// </summary>
        public bool NotificationsEnabled { get; set; }

        #endregion

        #region Events

        /// <summary>
        ///     Fired when any preference changes. Parameter is the property name that changed.
        /// </summary>
        public event Action<string> PreferenceChanged;

        #endregion

        #region Transcript Settings

        /// <summary>
        ///     Whether transcript routing is enabled.
        /// </summary>
        public bool TranscriptEnabled { get; set; }

        #endregion

        #region Audio Settings

        /// <summary>
        ///     Master volume for character audio (0.0 to 1.0).
        /// </summary>
        public float CharacterAudioVolume { get; set; }

        /// <summary>
        ///     Whether to play audio feedback sounds (e.g., listening indicator).
        /// </summary>
        public bool AudioFeedbackEnabled { get; set; }

        #endregion
    }
}
