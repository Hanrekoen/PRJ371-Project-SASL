using System;

namespace Convai.Runtime.Core.Configuration
{
    /// <summary>
    ///     Default implementation that returns fallback values for all features.
    ///     Used when no remote config provider is configured.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This is a no-op implementation that always returns the fallback value.
    ///         It is registered by default and can be replaced with a custom provider
    ///         that integrates with a remote config service.
    ///     </para>
    ///     <para>
    ///         <b>Thread Safety</b>: This implementation is inherently thread-safe
    ///         as it has no mutable state.
    ///     </para>
    /// </remarks>
    internal sealed class StaticFeatureVariantProvider : IFeatureVariantProvider
    {
        /// <inheritdoc />
        public bool IsEnabled(string featureKey) => false;

        /// <inheritdoc />
        public string GetVariant(string featureKey, string fallback = null) => fallback;

        /// <inheritdoc />
        public T GetValue<T>(string featureKey, T fallback = default) => fallback;

        /// <inheritdoc />
        /// <remarks>
        ///     This event is never raised by the static provider since values never change.
        ///     The no-op event accessors prevent memory leaks from abandoned subscriptions.
        /// </remarks>
        public event Action<FeatureVariantChanged> VariantChanged
        {
            add { } // No-op: static provider never fires events
            remove { }
        }
    }
}
