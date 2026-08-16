using System;

namespace Convai.Runtime.Core.Configuration
{
    /// <summary>
    ///     Default implementation of <see cref="IRuntimePreferences" />.
    ///     Stores preferences in memory with change notification.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This implementation stores preferences in memory only. For persistent storage,
    ///         wrap this class or use a persistence-backed implementation.
    ///     </para>
    ///     <para>
    ///         <b>Thread Safety</b>: Property setters use simple field assignment which is atomic
    ///         for the types used. Events are invoked synchronously on the calling thread.
    ///     </para>
    /// </remarks>
    internal sealed class RuntimePreferences : IRuntimePreferences
    {
        private bool _audioFeedbackEnabled = true;
        private float _characterAudioVolume = 1.0f;
        private bool _notificationsEnabled = true;
        private string _preferredMicrophoneDeviceId;
        private bool _transcriptEnabled = true;

        #region Microphone Settings

        /// <inheritdoc />
        public string PreferredMicrophoneDeviceId
        {
            get => _preferredMicrophoneDeviceId;
            set
            {
                if (_preferredMicrophoneDeviceId == value) return;
                _preferredMicrophoneDeviceId = value;
                OnPreferenceChanged(nameof(PreferredMicrophoneDeviceId));
            }
        }

        #endregion

        #region Notification Settings

        /// <inheritdoc />
        public bool NotificationsEnabled
        {
            get => _notificationsEnabled;
            set
            {
                if (_notificationsEnabled == value) return;
                _notificationsEnabled = value;
                OnPreferenceChanged(nameof(NotificationsEnabled));
            }
        }

        #endregion

        #region Transcript Settings

        /// <inheritdoc />
        public bool TranscriptEnabled
        {
            get => _transcriptEnabled;
            set
            {
                if (_transcriptEnabled == value) return;
                _transcriptEnabled = value;
                OnPreferenceChanged(nameof(TranscriptEnabled));
            }
        }

        #endregion

        #region Audio Settings

        /// <inheritdoc />
        public float CharacterAudioVolume
        {
            get => _characterAudioVolume;
            set
            {
                // Clamp to valid range
                float clampedValue = Math.Max(0f, Math.Min(1f, value));
                if (Math.Abs(_characterAudioVolume - clampedValue) < 0.0001f) return;
                _characterAudioVolume = clampedValue;
                OnPreferenceChanged(nameof(CharacterAudioVolume));
            }
        }

        /// <inheritdoc />
        public bool AudioFeedbackEnabled
        {
            get => _audioFeedbackEnabled;
            set
            {
                if (_audioFeedbackEnabled == value) return;
                _audioFeedbackEnabled = value;
                OnPreferenceChanged(nameof(AudioFeedbackEnabled));
            }
        }

        #endregion

        #region Events

        /// <inheritdoc />
        public event Action<string> PreferenceChanged;

        private void OnPreferenceChanged(string propertyName) => PreferenceChanged?.Invoke(propertyName);

        #endregion
    }
}
