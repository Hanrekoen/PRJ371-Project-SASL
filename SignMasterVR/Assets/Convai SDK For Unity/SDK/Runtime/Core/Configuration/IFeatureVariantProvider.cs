using System;

namespace Convai.Runtime.Core.Configuration
{
    /// <summary>
    ///     Provider for feature flags and A/B test variants.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The default implementation (<see cref="StaticFeatureVariantProvider" />) returns fallback values.
    ///         Custom implementations can integrate with remote config providers such as:
    ///     </para>
    ///     <list type="bullet">
    ///         <item>
    ///             <description>Firebase Remote Config</description>
    ///         </item>
    ///         <item>
    ///             <description>LaunchDarkly</description>
    ///         </item>
    ///         <item>
    ///             <description>Unity Remote Config</description>
    ///         </item>
    ///         <item>
    ///             <description>Custom backend services</description>
    ///         </item>
    ///     </list>
    ///     <para>
    ///         <b>Thread Safety</b>: Implementations must be thread-safe for read operations.
    ///         The <see cref="VariantChanged" /> event should be raised on the main thread.
    ///     </para>
    /// </remarks>
    public interface IFeatureVariantProvider
    {
        /// <summary>
        ///     Check if a feature is enabled.
        /// </summary>
        /// <param name="featureKey">Feature flag key (e.g., "new_chat_ui", "voice_enhancement").</param>
        /// <returns>True if enabled, false otherwise (including if key not found).</returns>
        public bool IsEnabled(string featureKey);

        /// <summary>
        ///     Get the variant string for a feature.
        /// </summary>
        /// <param name="featureKey">Feature flag key.</param>
        /// <param name="fallback">Value to return if feature key not found. Defaults to null.</param>
        /// <returns>Variant string or fallback if not found.</returns>
        /// <remarks>
        ///     Use this for A/B test variants where you need the specific variant name,
        ///     not just an enabled/disabled boolean.
        /// </remarks>
        public string GetVariant(string featureKey, string fallback = null);

        /// <summary>
        ///     Get a typed value for a feature.
        /// </summary>
        /// <typeparam name="T">Expected value type (bool, int, float, string).</typeparam>
        /// <param name="featureKey">Feature flag key.</param>
        /// <param name="fallback">Value to return if feature key not found or conversion fails.</param>
        /// <returns>Typed value or fallback.</returns>
        public T GetValue<T>(string featureKey, T fallback = default);

        /// <summary>
        ///     Fired when a feature variant changes (e.g., from remote config update).
        /// </summary>
        /// <remarks>
        ///     Subscribe to this event to react to live config changes.
        ///     Note that not all providers support live updates.
        /// </remarks>
        public event Action<FeatureVariantChanged> VariantChanged;
    }
}
