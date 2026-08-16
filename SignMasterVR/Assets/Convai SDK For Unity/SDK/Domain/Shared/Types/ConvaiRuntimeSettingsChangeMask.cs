using System;

namespace Convai.Shared.Types
{
    /// <summary>
    ///     Change mask for runtime settings updates.
    /// </summary>
    [Flags]
    public enum ConvaiRuntimeSettingsChangeMask
    {
        /// <summary>No settings changed.</summary>
        None = 0,

        /// <summary>Player display name changed.</summary>
        PlayerDisplayName = 1 << 0,

        /// <summary>Transcript enabled state changed.</summary>
        TranscriptEnabled = 1 << 1,

        /// <summary>Notifications enabled state changed.</summary>
        NotificationsEnabled = 1 << 2,

        /// <summary>Preferred microphone device ID changed.</summary>
        PreferredMicrophoneDeviceId = 1 << 3,

        /// <summary>All settings changed.</summary>
        All = PlayerDisplayName | TranscriptEnabled | NotificationsEnabled | PreferredMicrophoneDeviceId
    }
}
