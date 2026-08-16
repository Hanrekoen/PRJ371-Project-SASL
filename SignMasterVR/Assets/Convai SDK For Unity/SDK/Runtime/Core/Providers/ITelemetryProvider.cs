using System.Collections.Generic;

namespace Convai.Runtime.Core.Providers
{
    /// <summary>
    ///     Provider for sending telemetry and analytics events.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The default implementation (<see cref="NullTelemetryProvider" />) is a no-op.
    ///         Custom implementations can integrate with analytics services:
    ///     </para>
    ///     <list type="bullet">
    ///         <item>
    ///             <description>Unity Analytics</description>
    ///         </item>
    ///         <item>
    ///             <description>Firebase Analytics</description>
    ///         </item>
    ///         <item>
    ///             <description>Amplitude</description>
    ///         </item>
    ///         <item>
    ///             <description>Mixpanel</description>
    ///         </item>
    ///         <item>
    ///             <description>Custom backend</description>
    ///         </item>
    ///     </list>
    ///     <para>
    ///         <b>Privacy</b>: Implementations should respect user privacy settings
    ///         and only collect data when consent is given.
    ///     </para>
    /// </remarks>
    public interface ITelemetryProvider
    {
        #region Timing

        /// <summary>
        ///     Tracks a timed event duration.
        /// </summary>
        /// <param name="category">Event category.</param>
        /// <param name="name">Event name.</param>
        /// <param name="durationMs">Duration in milliseconds.</param>
        public void TrackTiming(string category, string name, long durationMs);

        #endregion

        #region Events

        /// <summary>
        ///     Tracks a custom event.
        /// </summary>
        /// <param name="eventName">Name of the event (e.g., "conversation_started").</param>
        /// <param name="properties">Optional event properties.</param>
        public void TrackEvent(string eventName, IDictionary<string, object> properties = null);

        /// <summary>
        ///     Tracks a screen or page view.
        /// </summary>
        /// <param name="screenName">Name of the screen.</param>
        public void TrackScreen(string screenName);

        #endregion

        #region User Properties

        /// <summary>
        ///     Sets a user property.
        /// </summary>
        /// <param name="propertyName">Property name.</param>
        /// <param name="value">Property value.</param>
        public void SetUserProperty(string propertyName, string value);

        /// <summary>
        ///     Sets the user ID for analytics correlation.
        /// </summary>
        /// <param name="userId">User identifier (should be anonymized).</param>
        public void SetUserId(string userId);

        #endregion

        #region Lifecycle

        /// <summary>
        ///     Flushes any pending telemetry events.
        /// </summary>
        public void Flush();

        /// <summary>
        ///     Whether telemetry collection is currently enabled.
        /// </summary>
        public bool IsEnabled { get; }

        /// <summary>
        ///     Enables or disables telemetry collection.
        /// </summary>
        public void SetEnabled(bool enabled);

        #endregion
    }
}
