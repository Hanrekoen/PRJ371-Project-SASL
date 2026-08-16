using System.Collections.Generic;

namespace Convai.Runtime.Core.Providers
{
    /// <summary>
    ///     Default no-op telemetry provider.
    ///     Registered by default when no custom provider is configured.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This implementation discards all telemetry events without sending them anywhere.
    ///         Replace with a real implementation to enable analytics.
    ///     </para>
    ///     <para>
    ///         <b>Thread Safety</b>: This implementation is inherently thread-safe as it has no state.
    ///     </para>
    /// </remarks>
    internal sealed class NullTelemetryProvider : ITelemetryProvider
    {
        /// <inheritdoc />
        public void TrackEvent(string eventName, IDictionary<string, object> properties = null)
        {
            // No-op
        }

        /// <inheritdoc />
        public void TrackScreen(string screenName)
        {
            // No-op
        }

        /// <inheritdoc />
        public void SetUserProperty(string propertyName, string value)
        {
            // No-op
        }

        /// <inheritdoc />
        public void SetUserId(string userId)
        {
            // No-op
        }

        /// <inheritdoc />
        public void TrackTiming(string category, string name, long durationMs)
        {
            // No-op
        }

        /// <inheritdoc />
        public void Flush()
        {
            // No-op
        }

        /// <inheritdoc />
        public bool IsEnabled { get; private set; }

        /// <inheritdoc />
        public void SetEnabled(bool enabled) => IsEnabled = enabled;
    }
}
