namespace Convai.Shared.Types
{
    /// <summary>
    ///     Result payload for applying runtime settings.
    /// </summary>
    public readonly struct ConvaiRuntimeSettingsApplyResult
    {
        /// <summary>Gets whether the apply operation succeeded.</summary>
        public bool Success { get; }

        /// <summary>Gets the resulting settings snapshot after the apply.</summary>
        public ConvaiRuntimeSettingsSnapshot Snapshot { get; }

        /// <summary>Gets the bitmask of settings that were actually applied.</summary>
        public ConvaiRuntimeSettingsChangeMask AppliedMask { get; }

        /// <summary>Gets a validation message, or empty if successful.</summary>
        public string ValidationMessage { get; }

        /// <summary>Creates a new apply result.</summary>
        public ConvaiRuntimeSettingsApplyResult(
            bool success,
            ConvaiRuntimeSettingsSnapshot snapshot,
            ConvaiRuntimeSettingsChangeMask appliedMask,
            string validationMessage)
        {
            Success = success;
            Snapshot = snapshot;
            AppliedMask = appliedMask;
            ValidationMessage = validationMessage ?? string.Empty;
        }

        /// <summary>Creates a successful apply result.</summary>
        public static ConvaiRuntimeSettingsApplyResult Ok(
            ConvaiRuntimeSettingsSnapshot snapshot,
            ConvaiRuntimeSettingsChangeMask appliedMask) =>
            new(true, snapshot, appliedMask, string.Empty);

        /// <summary>Creates a failed apply result with a validation message.</summary>
        public static ConvaiRuntimeSettingsApplyResult Invalid(
            ConvaiRuntimeSettingsSnapshot snapshot,
            string message) =>
            new(false, snapshot, ConvaiRuntimeSettingsChangeMask.None, message);
    }
}
