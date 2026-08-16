namespace Convai.Shared.Types
{
    /// <summary>
    ///     Persisted user override payload.
    ///     Null values indicate "use defaults".
    /// </summary>
    public sealed class ConvaiRuntimeSettingsOverrides
    {
        /// <summary>Gets or sets the player display name override, or null to use the default.</summary>
        public string PlayerDisplayName { get; set; }

        /// <summary>Gets or sets the transcript enabled override, or null to use the default.</summary>
        public bool? TranscriptEnabled { get; set; }

        /// <summary>Gets or sets the notifications enabled override, or null to use the default.</summary>
        public bool? NotificationsEnabled { get; set; }

        /// <summary>Gets or sets the preferred microphone device ID override, or null to use the default.</summary>
        public string PreferredMicrophoneDeviceId { get; set; }

    }
}
