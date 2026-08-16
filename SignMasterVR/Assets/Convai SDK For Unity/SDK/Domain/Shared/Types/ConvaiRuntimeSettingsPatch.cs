namespace Convai.Shared.Types
{
    /// <summary>
    ///     Patch payload for runtime settings updates.
    /// </summary>
    public sealed class ConvaiRuntimeSettingsPatch
    {
        /// <summary>Gets or sets the player display name override, or null to leave unchanged.</summary>
        public string PlayerDisplayName { get; set; }

        /// <summary>Gets or sets the transcript routing override, or null to leave unchanged.</summary>
        public bool? TranscriptEnabled { get; set; }

        /// <summary>Gets or sets the notifications enabled override, or null to leave unchanged.</summary>
        public bool? NotificationsEnabled { get; set; }

        /// <summary>Gets or sets the preferred microphone device ID override, or null to leave unchanged.</summary>
        public string PreferredMicrophoneDeviceId { get; set; }

        /// <summary>Gets whether this patch contains no overrides.</summary>
        public bool IsEmpty =>
            PlayerDisplayName == null &&
            TranscriptEnabled == null &&
            NotificationsEnabled == null &&
            PreferredMicrophoneDeviceId == null;
    }
}
