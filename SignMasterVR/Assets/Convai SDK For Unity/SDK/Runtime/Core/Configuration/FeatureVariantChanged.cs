namespace Convai.Runtime.Core.Configuration
{
    /// <summary>
    ///     Event raised when a feature variant changes at runtime.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This struct is used by <see cref="IFeatureVariantProvider" /> to notify subscribers
    ///         when a feature flag or A/B test variant changes, typically due to a remote config update.
    ///     </para>
    ///     <para>
    ///         <b>Thread Safety</b>: As a readonly struct, this type is inherently thread-safe
    ///         and can be safely passed between threads.
    ///     </para>
    /// </remarks>
    public readonly struct FeatureVariantChanged
    {
        /// <summary>
        ///     Creates a new feature variant changed event.
        /// </summary>
        public FeatureVariantChanged(string featureKey, string oldValue, string newValue)
        {
            FeatureKey = featureKey ?? string.Empty;
            OldValue = oldValue;
            NewValue = newValue;
        }

        /// <summary>
        ///     The feature key that changed.
        /// </summary>
        public string FeatureKey { get; }

        /// <summary>
        ///     The previous value of the feature variant (null if newly added).
        /// </summary>
        public string OldValue { get; }

        /// <summary>
        ///     The new value of the feature variant (null if removed).
        /// </summary>
        public string NewValue { get; }

        /// <summary>
        ///     Whether the feature was enabled before the change.
        /// </summary>
        public bool WasEnabled => !string.IsNullOrEmpty(OldValue) && OldValue != "false" && OldValue != "0";

        /// <summary>
        ///     Whether the feature is enabled after the change.
        /// </summary>
        public bool IsEnabled => !string.IsNullOrEmpty(NewValue) && NewValue != "false" && NewValue != "0";

        /// <summary>
        ///     Returns a human-readable summary of the change.
        /// </summary>
        public override string ToString() =>
            $"FeatureVariantChanged {{ Key = {FeatureKey}, OldValue = {OldValue ?? "null"}, NewValue = {NewValue ?? "null"} }}";
    }
}
