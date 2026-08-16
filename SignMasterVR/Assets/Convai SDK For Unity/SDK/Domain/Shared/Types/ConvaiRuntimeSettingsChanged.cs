namespace Convai.Shared.Types
{
    /// <summary>
    ///     Describes a runtime settings change.
    /// </summary>
    public readonly struct ConvaiRuntimeSettingsChanged
    {
        /// <summary>Gets the settings snapshot before the change.</summary>
        public ConvaiRuntimeSettingsSnapshot Previous { get; }

        /// <summary>Gets the settings snapshot after the change.</summary>
        public ConvaiRuntimeSettingsSnapshot Current { get; }

        /// <summary>Gets the bitmask indicating which settings changed.</summary>
        public ConvaiRuntimeSettingsChangeMask Mask { get; }

        /// <summary>Creates a new change descriptor.</summary>
        public ConvaiRuntimeSettingsChanged(
            ConvaiRuntimeSettingsSnapshot previous,
            ConvaiRuntimeSettingsSnapshot current,
            ConvaiRuntimeSettingsChangeMask mask)
        {
            Previous = previous;
            Current = current;
            Mask = mask;
        }
    }
}
